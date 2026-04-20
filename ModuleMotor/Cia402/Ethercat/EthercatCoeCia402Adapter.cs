using System.Buffers.Binary;
using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Ethercat.Soem;

namespace ModuleMotor.Cia402.Ethercat
{
    /// <summary>
    /// CiA 402 adapter for an eRob joint connected via EtherCAT (CoE).
    /// Implements both <see cref="ICia402ObjectAccess"/> (SDO mailbox) and
    /// <see cref="ICia402ProcessData"/> (PDO cyclic exchange) on top of SOEM.
    ///
    /// Usage:
    /// <code>
    ///   var master  = new SoemMaster(@"\Device\NPF_{...}", cyclePeriodMs: 1);
    ///   var adapter = new EthercatCoeCia402Adapter(master, slaveIndex: 1);
    ///   await master.OpenAsync(ct);
    ///   var controller = DriveControllerFactory.CreateCia402Controller(adapter, adapter);
    /// </code>
    /// </summary>
    public sealed class EthercatCoeCia402Adapter : ICia402ObjectAccess, ICia402ProcessData
    {
        private readonly SoemMaster _master;
        private readonly ushort _slaveIndex;
        private readonly ErobPdoMap _pdo;

        /// <param name="master">An opened <see cref="SoemMaster"/> instance.</param>
        /// <param name="slaveIndex">1-based EtherCAT slave position on the bus.</param>
        /// <param name="pdoMap">
        /// Optional custom PDO layout — defaults to <see cref="ErobPdoMap.Default"/>.
        /// Override if your eRob firmware uses a non-default PDO mapping.
        /// </param>
        public EthercatCoeCia402Adapter(
            SoemMaster master,
            ushort slaveIndex = 1,
            ErobPdoMap? pdoMap = null)
        {
            _master = master;
            _slaveIndex = slaveIndex;
            _pdo = pdoMap ?? ErobPdoMap.Default;
        }

        // ── ICia402ObjectAccess — CoE SDO mailbox ─────────────────────────────

        /// <summary>
        /// Reads a CiA 402 object via CoE SDO mailbox (non-cyclic, up to ~700 ms).
        /// Use for configuration and state-machine commands, not cyclic data.
        /// </summary>
        public async Task<ObjectValue> ReadAsync(ushort index, byte subIndex, CancellationToken ct)
        {
            // Max SDO payload for a single object is 4 bytes for most CiA 402 objects.
            // Allocate 8 to handle any vendor-specific objects up to 8 bytes.
            byte[] raw = await _master.SdoReadAsync(_slaveIndex, index, subIndex, 8, ct);
            return new ObjectValue(raw);
        }

        /// <summary>
        /// Writes a CiA 402 object via CoE SDO mailbox.
        /// </summary>
        public async Task WriteAsync(ushort index, byte subIndex, ObjectValue value, CancellationToken ct)
        {
            await _master.SdoWriteAsync(_slaveIndex, index, subIndex, value.Raw, ct);
        }

        // ── ICia402ProcessData — PDO cyclic exchange ──────────────────────────

        /// <summary>
        /// Reads the latest input process data snapshot from the IO map.
        /// Returns immediately (no SDO round-trip); data is updated each PDO cycle.
        /// </summary>
        public ValueTask<ProcessSnapshot> ReadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            byte[] inputs = _master.ReadInputs(_pdo.InRegionOffset, _pdo.InBytes);

            var snapshot = new ProcessSnapshot(
                Statusword:          BinaryPrimitives.ReadUInt16LittleEndian(inputs.AsSpan(_pdo.InStatusword)),
                OperationModeDisplay:(sbyte)inputs[_pdo.InModesOfOperationDisplay],
                PositionActualValue: BinaryPrimitives.ReadInt32LittleEndian(inputs.AsSpan(_pdo.InPositionActualValue)),
                VelocityActualValue: BinaryPrimitives.ReadInt32LittleEndian(inputs.AsSpan(_pdo.InVelocityActualValue)),
                TorqueActualValue:   BinaryPrimitives.ReadInt16LittleEndian(inputs.AsSpan(_pdo.InTorqueActualValue)),
                ErrorCode:           BinaryPrimitives.ReadUInt16LittleEndian(inputs.AsSpan(_pdo.InErrorCode)));

            return ValueTask.FromResult(snapshot);
        }

        /// <summary>
        /// Writes updated fields from <paramref name="command"/> into the output IO map.
        /// Only non-null fields are written; others retain their previous values.
        /// The cyclic thread will transmit the updated outputs on the next PDO frame.
        /// </summary>
        public ValueTask WriteAsync(ProcessCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Build a mutable copy of the current output region so we can apply
            // only the fields present in the command (sparse update).
            byte[] outputs = new byte[_pdo.OutBytes];
            _master.ReadOutputs(_pdo.OutRegionOffset, outputs);

            if (command.Controlword is ushort cw)
                BinaryPrimitives.WriteUInt16LittleEndian(outputs.AsSpan(_pdo.OutControlword), cw);

            if (command.OperationMode is sbyte mode)
                outputs[_pdo.OutModesOfOperation] = (byte)mode;

            if (command.TargetPosition is int pos)
                BinaryPrimitives.WriteInt32LittleEndian(outputs.AsSpan(_pdo.OutTargetPosition), pos);

            if (command.TargetVelocity is int vel)
                BinaryPrimitives.WriteInt32LittleEndian(outputs.AsSpan(_pdo.OutTargetVelocity), vel);

            if (command.TargetTorque is short torque)
                BinaryPrimitives.WriteInt16LittleEndian(outputs.AsSpan(_pdo.OutTargetTorque), torque);

            _master.WriteOutputs(_pdo.OutRegionOffset, outputs);

            return ValueTask.CompletedTask;
        }
    }
}
