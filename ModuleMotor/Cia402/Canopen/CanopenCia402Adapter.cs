using System.Buffers.Binary;
using ModuleMotor.Canopen;
using ModuleMotor.Canopen.Transport;
using ModuleMotor.Cia402.Abstractions;

namespace ModuleMotor.Cia402.Canopen
{
    /// <summary>
    /// CiA 402 adapter for an eRob joint connected via CANopen.
    /// Implements <see cref="ICia402ObjectAccess"/> using expedited SDO (mailbox)
    /// and <see cref="ICia402ProcessData"/> using SYNC-triggered PDO exchange.
    ///
    /// Usage:
    /// <code>
    ///   var transport = new VCanMotorModelTransport(motorModel);
    ///   var adapter   = new CanopenCia402Adapter(nodeId: 1, transport);
    ///   await adapter.OpenAsync(ct);          // NMT Start Remote Node
    ///   var controller = DriveControllerFactory.CreateCia402Controller(adapter, adapter);
    /// </code>
    /// </summary>
    public sealed class CanopenCia402Adapter : ICia402ObjectAccess, ICia402ProcessData
    {
        private static readonly TimeSpan PdoTimeout = TimeSpan.FromMilliseconds(50);

        private readonly byte _nodeId;
        private readonly CanopenSdoClient _sdoClient;
        private readonly CanopenNmtClient _nmtClient;
        private readonly IVCanFrameTransport _transport;
        private readonly ErobCanopenPdoMap _pdo;

        /// <param name="nodeId">CANopen node ID of the eRob joint (1–127).</param>
        /// <param name="transport">VCan frame transport wrapping the connected MotorModel.</param>
        /// <param name="pdoMap">
        /// Optional custom PDO layout — defaults to <see cref="ErobCanopenPdoMap.Default"/>.
        /// Override if your eRob firmware uses a non-default PDO mapping.
        /// </param>
        public CanopenCia402Adapter(
            byte nodeId,
            IVCanFrameTransport transport,
            ErobCanopenPdoMap? pdoMap = null)
        {
            _nodeId = nodeId;
            _transport = transport;
            _sdoClient = new CanopenSdoClient(transport);
            _nmtClient = new CanopenNmtClient(transport);
            _pdo = pdoMap ?? ErobCanopenPdoMap.Default;
        }

        // ── NMT lifecycle ─────────────────────────────────────────────────────

        /// <summary>
        /// Sends NMT <c>Start Remote Node</c> (0x01) to bring the eRob node to
        /// NMT Operational state. Must be called before executing drive commands
        /// that rely on PDO exchange or CiA 402 state transitions.
        /// </summary>
        public Task OpenAsync(CancellationToken ct)
            => _nmtClient.StartRemoteNodeAsync(_nodeId, ct);

        /// <summary>
        /// Sends NMT <c>Stop Remote Node</c> (0x02) to return the eRob to
        /// NMT Stopped state and disable PDO exchange.
        /// </summary>
        public Task CloseAsync(CancellationToken ct)
            => _nmtClient.StopRemoteNodeAsync(_nodeId, ct);

        // ── ICia402ObjectAccess — expedited SDO ───────────────────────────────

        /// <summary>
        /// Reads a CiA 402 object via CANopen expedited SDO (COB-ID 0x600+nodeId).
        /// Supports objects up to 4 bytes. Use for configuration and state-machine
        /// commands, not for cyclic real-time data.
        /// </summary>
        public async Task<ObjectValue> ReadAsync(ushort index, byte subIndex, CancellationToken ct)
        {
            byte[] raw = await _sdoClient.ReadAsync(_nodeId, index, subIndex, ct);
            return new ObjectValue(raw);
        }

        /// <summary>
        /// Writes a CiA 402 object via CANopen expedited SDO.
        /// </summary>
        public async Task WriteAsync(ushort index, byte subIndex, ObjectValue value, CancellationToken ct)
        {
            await _sdoClient.WriteAsync(_nodeId, index, subIndex, value.Raw, ct);
        }

        // ── ICia402ProcessData — SYNC-triggered PDO ───────────────────────────

        /// <summary>
        /// Sends a SYNC frame (0x080) and waits for TPDO1 and TPDO2 responses
        /// from the eRob node, then assembles them into a <see cref="ProcessSnapshot"/>.
        /// </summary>
        public async ValueTask<ProcessSnapshot> ReadAsync(CancellationToken ct)
        {
            uint tpdo1CobId = (uint)(0x180 + _nodeId);
            uint tpdo2CobId = (uint)(0x280 + _nodeId);

            VCanReadCursor cursor = _transport.CaptureCursor();
            await _transport.SendAsync(CanopenFrameBuilder.BuildSync(), ct);

            // Receive TPDO1 and TPDO2 concurrently (both arrive after the same SYNC)
            Task<VCanReceivedFrame> tpdo1Task = _transport.WaitForFrameAsync(
                f => f.CanId == tpdo1CobId, cursor, PdoTimeout, ct);
            Task<VCanReceivedFrame> tpdo2Task = _transport.WaitForFrameAsync(
                f => f.CanId == tpdo2CobId, cursor, PdoTimeout, ct);

            await Task.WhenAll(tpdo1Task, tpdo2Task);

            byte[] t1 = tpdo1Task.Result.Payload;
            byte[] t2 = tpdo2Task.Result.Payload;

            return new ProcessSnapshot(
                Statusword:          BinaryPrimitives.ReadUInt16LittleEndian(t1.AsSpan(_pdo.Tpdo1Statusword)),
                OperationModeDisplay:(sbyte)t1[_pdo.Tpdo1ModesOfOperationDisplay],
                PositionActualValue: BinaryPrimitives.ReadInt32LittleEndian(t1.AsSpan(_pdo.Tpdo1PositionActualValue)),
                VelocityActualValue: BinaryPrimitives.ReadInt32LittleEndian(t2.AsSpan(_pdo.Tpdo2VelocityActualValue)),
                TorqueActualValue:   BinaryPrimitives.ReadInt16LittleEndian(t2.AsSpan(_pdo.Tpdo2TorqueActualValue)),
                ErrorCode:           BinaryPrimitives.ReadUInt16LittleEndian(t2.AsSpan(_pdo.Tpdo2ErrorCode)));
        }

        /// <summary>
        /// Writes updated fields from <paramref name="command"/> to the eRob node
        /// using RPDO1 and RPDO2 frames. Only non-null fields are sent; the last
        /// cached frame values are preserved for unchanged fields.
        /// </summary>
        public async ValueTask WriteAsync(ProcessCommand command, CancellationToken ct)
        {
            // RPDO1: Controlword | ModesOfOperation | pad | TargetPosition
            if (command.Controlword.HasValue
                || command.OperationMode.HasValue
                || command.TargetPosition.HasValue)
            {
                byte[] rpdo1 = new byte[_pdo.Rpdo1Bytes];
                if (command.Controlword is ushort cw)
                    BinaryPrimitives.WriteUInt16LittleEndian(rpdo1.AsSpan(_pdo.Rpdo1Controlword), cw);
                if (command.OperationMode is sbyte mode)
                    rpdo1[_pdo.Rpdo1ModesOfOperation] = (byte)mode;
                if (command.TargetPosition is int pos)
                    BinaryPrimitives.WriteInt32LittleEndian(rpdo1.AsSpan(_pdo.Rpdo1TargetPosition), pos);

                await _transport.SendAsync(CanopenFrameBuilder.BuildRpdo1(_nodeId, rpdo1), ct);
            }

            // RPDO2: TargetVelocity | TargetTorque
            if (command.TargetVelocity.HasValue || command.TargetTorque.HasValue)
            {
                byte[] rpdo2 = new byte[_pdo.Rpdo2Bytes];
                if (command.TargetVelocity is int vel)
                    BinaryPrimitives.WriteInt32LittleEndian(rpdo2.AsSpan(_pdo.Rpdo2TargetVelocity), vel);
                if (command.TargetTorque is short torque)
                    BinaryPrimitives.WriteInt16LittleEndian(rpdo2.AsSpan(_pdo.Rpdo2TargetTorque), torque);

                await _transport.SendAsync(CanopenFrameBuilder.BuildRpdo2(_nodeId, rpdo2), ct);
            }
        }
    }
}
