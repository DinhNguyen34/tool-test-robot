using System.Buffers.Binary;
using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Ethercat.Soem;
using System.Threading;

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
    public sealed class EthercatCoeCia402Adapter : ICia402ObjectAccess, ICia402ProcessData, ICia402ProcessDataCapabilities
    {
        private readonly SoemMaster _master;
        private readonly ushort _slaveIndex;
        private readonly ErobPdoMap _pdo;
        private readonly SemaphoreSlim _pdoWriteLock = new(1, 1);
        private readonly int _minInputBytes;

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
            if (slaveIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(slaveIndex), "EtherCAT slave index must be 1-based.");

            _master = master;
            _slaveIndex = slaveIndex;
            _pdo = pdoMap ?? ErobPdoMap.Default;
            _minInputBytes = new[]
            {
                MinBytes(_pdo.InErrorCode,               sizeof(ushort)),
                MinBytes(_pdo.InStatusword,              sizeof(ushort)),
                MinBytes(_pdo.InPositionActualValue,     sizeof(int)),
                MinBytes(_pdo.InVelocityActualValue,     sizeof(int)),
                MinBytes(_pdo.InTorqueActualValue,       sizeof(short)),
                MinBytes(_pdo.InModesOfOperationDisplay, sizeof(byte))
            }.Max();
        }

        private static int MinBytes(int offset, int size)
            => offset >= 0 ? offset + size : 0;

        public bool HasTargetPosition => _pdo.OutTargetPosition >= 0;
        public bool HasTargetVelocity => _pdo.OutTargetVelocity >= 0;
        public bool HasTargetTorque => _pdo.OutTargetTorque >= 0;
        public bool HasMaxTorque => _pdo.OutMaxTorque >= 0;
        public bool HasControlword => _pdo.OutControlword >= 0;
        public bool HasOperationMode => _pdo.OutModesOfOperation >= 0;
        public bool HasOperationModeDisplay => _pdo.InModesOfOperationDisplay >= 0;
        public bool HasVelocityActualValue => _pdo.InVelocityActualValue >= 0;
        public bool HasTorqueActualValue => _pdo.InTorqueActualValue >= 0;
        public bool HasErrorCode => _pdo.InErrorCode >= 0;

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

            int inputOffset = GetInputRegionOffset();
            byte[] inputs = _master.ReadInputs(inputOffset, _pdo.InBytes);
            ValidateInputLength(inputs);

            var snapshot = new ProcessSnapshot(
                Statusword:          ReadU16(inputs, _pdo.InStatusword),
                OperationModeDisplay:_pdo.InModesOfOperationDisplay >= 0 ? (sbyte)inputs[_pdo.InModesOfOperationDisplay] : (sbyte)0,
                PositionActualValue: ReadI32(inputs, _pdo.InPositionActualValue),
                VelocityActualValue: ReadI32(inputs, _pdo.InVelocityActualValue),
                TorqueActualValue:   ReadI16(inputs, _pdo.InTorqueActualValue),
                ErrorCode:           ReadU16(inputs, _pdo.InErrorCode));

            return ValueTask.FromResult(snapshot);
        }

        private static ushort ReadU16(byte[] data, int offset)
            => offset >= 0 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)) : (ushort)0;

        private static short ReadI16(byte[] data, int offset)
            => offset >= 0 ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)) : (short)0;

        private static int ReadI32(byte[] data, int offset)
            => offset >= 0 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)) : 0;

        private static void WriteMappedI32(byte[] data, int offset, int value, string fieldName)
        {
            if (offset < 0)
                throw new InvalidOperationException($"{fieldName} is not mapped in this drive's RPDO; write the corresponding object via SDO instead.");
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
        }

        private static void WriteMappedI16(byte[] data, int offset, short value, string fieldName)
        {
            if (offset < 0)
                throw new InvalidOperationException($"{fieldName} is not mapped in this drive's RPDO; write the corresponding object via SDO instead.");
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), value);
        }

        private static void WriteMappedU16(byte[] data, int offset, ushort value, string fieldName)
        {
            if (offset < 0)
                throw new InvalidOperationException($"{fieldName} is not mapped in this drive's RPDO; write the corresponding object via SDO instead.");
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        }

        /// <summary>
        /// Writes updated fields from <paramref name="command"/> into the output IO map.
        /// Only non-null fields are written; others retain their previous values.
        /// The cyclic thread will transmit the updated outputs on the next PDO frame.
        /// </summary>
        public async ValueTask WriteAsync(ProcessCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int outputOffset = GetOutputRegionOffset();

            await _pdoWriteLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Build a mutable copy of the current output region so we can apply
                // only the fields present in the command (sparse update).
                byte[] outputs = new byte[_pdo.OutBytes];
                _master.ReadOutputs(outputOffset, outputs);

                if (command.TargetPosition is int pos)
                    WriteMappedI32(outputs, _pdo.OutTargetPosition, pos, "TargetPosition");

                if (command.TargetVelocity is int vel)
                    WriteMappedI32(outputs, _pdo.OutTargetVelocity, vel, "TargetVelocity");

                if (command.TargetTorque is short torque)
                    WriteMappedI16(outputs, _pdo.OutTargetTorque, torque, "TargetTorque");

                if (command.MaxTorque is ushort maxTorque)
                    WriteMappedU16(outputs, _pdo.OutMaxTorque, maxTorque, "MaxTorque");

                if (command.Controlword is ushort cw)
                    WriteMappedU16(outputs, _pdo.OutControlword, cw, "Controlword");

                if (command.OperationMode is sbyte mode)
                {
                    if (_pdo.OutModesOfOperation < 0)
                        throw new InvalidOperationException("OperationMode is not mapped in this drive's RPDO; write 0x6060 via SDO instead.");
                    outputs[_pdo.OutModesOfOperation] = (byte)mode;
                }

                _master.WriteOutputs(outputOffset, outputs);
            }
            finally
            {
                _pdoWriteLock.Release();
            }
        }

        private int GetOutputRegionOffset()
        {
            int slaveOffset = (_slaveIndex - 1) * _pdo.OutStride;
            return _master.OutputRegionOffset + _pdo.OutRegionOffset + slaveOffset;
        }

        private int GetInputRegionOffset()
        {
            int slaveOffset = (_slaveIndex - 1) * _pdo.InStride;
            return _master.InputRegionOffset + _pdo.InRegionOffset + slaveOffset;
        }

        private void ValidateInputLength(byte[] inputs)
        {
            if (inputs.Length < _minInputBytes)
            {
                throw new InvalidOperationException(
                    $"EtherCAT TPDO payload too short for slave {_slaveIndex}: received {inputs.Length} byte(s), expected at least {_minInputBytes} byte(s).");
            }
        }
    }
}
