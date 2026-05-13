using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Models;

namespace ModuleMotor.Cia402.Core
{
    public sealed class Cia402DriveService
    {
        private const int DefaultProfileVelocity = 5566;
        private const int DefaultProfileAcceleration = 5566;
        private const int DefaultProfileDeceleration = 5566;
        private const ushort DefaultMaxTorque = 1000;

        private readonly ICia402ObjectAccess _objectAccess;
        private readonly ICia402ProcessData? _processData;
        private readonly ICia402ProcessDataCapabilities? _processDataCapabilities;
        private Cia402OperationMode? _lastRequestedMode;

        public Cia402DriveService(
            ICia402ObjectAccess objectAccess,
            ICia402ProcessData? processData = null)
        {
            _objectAccess = objectAccess;
            _processData = processData;
            _processDataCapabilities = processData as ICia402ProcessDataCapabilities;
        }

        public Task FaultResetAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.FaultReset, ct);

        public Task ShutdownAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.Shutdown, ct);

        public Task SwitchOnAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.SwitchOn, ct);

        public Task EnableOperationAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.EnableOperation, ct);

        public Task DisableOperationAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.DisableOperation, ct);
        public Task QuickStopAsync(CancellationToken ct)
            => WriteControlwordAsync(Cia402ControlwordBuilder.QuickStop, ct);

        public async Task SetModeAsync(Cia402OperationMode mode, CancellationToken ct)
        {
            await _objectAccess.WriteAsync(
                Cia402ObjectIndex.ModesOfOperation,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromSByte((sbyte)mode),
                ct);
            _lastRequestedMode = mode;

            await EnsurePositiveUInt16Async(Cia402ObjectIndex.MaxTorque, DefaultMaxTorque, ct);

            await TryWriteProcessDataAsync(new ProcessCommand(
                OperationMode: (sbyte)mode,
                MaxTorque: DefaultMaxTorque), ct);

            await PrepareModeDefaultsAsync(mode, ct);
        }

        public async Task SyncActualPositionToTargetAsync(CancellationToken ct)
        {
            ObjectValue actualPosition = await _objectAccess.ReadAsync(
                Cia402ObjectIndex.PositionActualValue,
                Cia402ObjectIndex.ValueSubIndex,
                ct);

            await WriteTargetPositionProfileAsync(actualPosition.AsInt32(), ct);
        }

        public async Task MoveAbsolutePositionAsync(
            int targetPosition,
            Cia402PositionCommandMode commandMode,
            bool immediateChange,
            TimeSpan ackTimeout,
            int? profileVelocity,
            int? profileAcceleration,
            int? profileDeceleration,
            CancellationToken ct)
        {
            if (commandMode == Cia402PositionCommandMode.CyclicSynchronousPosition)
            {
                await WriteTargetPositionPdoAsync(targetPosition, ct);
                return;
            }

            await ValidateMotionPreconditionsAsync(
                "Profile Position command",
                ct,
                Cia402OperationMode.ProfilePosition);
            await ApplyProfilePositionParametersAsync(
                profileVelocity,
                profileAcceleration,
                profileDeceleration,
                ct);
            await WriteTargetPositionProfileAsync(targetPosition, ct);
            ushort triggerWord = Cia402ControlwordBuilder.BuildProfilePositionTrigger(immediateChange, absoluteMove: true);
            bool triggerWritten = false;

            TimeSpan timeout = ackTimeout == default ? TimeSpan.FromSeconds(5) : ackTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            CancellationToken linkedCt = timeoutCts.Token;

            try
            {
                await WriteControlwordAsync(triggerWord, linkedCt);
                triggerWritten = true;

                while (true)
                {
                    linkedCt.ThrowIfCancellationRequested();
                    // In PDO mode, ReadStatuswordAsync reads the latest process data snapshot,
                    // which for CANopen means issuing a SYNC and waiting for TPDO1/TPDO2.
                    Cia402Statusword sw = await ReadStatuswordAsync(linkedCt);
                    if (sw.Fault)
                        throw new InvalidOperationException("Drive entered Fault state during position move");
                    if (sw.SetPointAcknowledge)
                        break;
                    await Task.Delay(5, linkedCt);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Drive did not acknowledge set point within {timeout.TotalSeconds:0.#}s.");
            }
            finally
            {
                if (triggerWritten)
                    await TryRestoreEnableOperationAsync();
            }
        }

        public async Task SetTargetVelocityAsync(int targetVelocity, CancellationToken ct)
        {
            sbyte modeDisplay = await ValidateVelocityPreconditionsAsync(ct);

            if (modeDisplay != (sbyte)Cia402OperationMode.ProfileVelocity
                && modeDisplay != (sbyte)Cia402OperationMode.CyclicSynchronousVelocity)
            {
                throw new InvalidOperationException(
                    $"Drive is not in velocity mode. 0x6061 ModesOfOperationDisplay={modeDisplay}; set 0x6060 to ProfileVelocity(3) or CyclicSynchronousVelocity(9) before sending TargetVelocity.");
            }

            if (modeDisplay == (sbyte)Cia402OperationMode.ProfileVelocity)
                await ValidateProfileVelocityRampAsync(ct);

            await WriteTargetVelocityAsync(targetVelocity, ct);
        }

        private async Task PrepareModeDefaultsAsync(Cia402OperationMode mode, CancellationToken ct)
        {
            switch (mode)
            {
                case Cia402OperationMode.ProfilePosition:
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileVelocity, DefaultProfileVelocity, ct);
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileAcceleration, DefaultProfileAcceleration, ct);
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileDeceleration, DefaultProfileDeceleration, ct);
                    break;
                case Cia402OperationMode.ProfileVelocity:
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileAcceleration, DefaultProfileAcceleration, ct);
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileDeceleration, DefaultProfileDeceleration, ct);
                    await WriteTargetVelocityAsync(0, ct);
                    break;
                case Cia402OperationMode.CyclicSynchronousVelocity:
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileAcceleration, DefaultProfileAcceleration, ct);
                    await EnsurePositiveInt32Async(Cia402ObjectIndex.ProfileDeceleration, DefaultProfileDeceleration, ct);
                    await WriteTargetVelocityAsync(0, ct);
                    break;
                case Cia402OperationMode.ProfileTorque:
                case Cia402OperationMode.CyclicSynchronousTorque:
                    await EnsurePositiveUInt16Async(Cia402ObjectIndex.MaxTorque, DefaultMaxTorque, ct);
                    await WriteTargetTorqueAsync(0, ct);
                    break;
            }
        }

        private async Task EnsurePositiveInt32Async(ushort index, int defaultValue, CancellationToken ct)
        {
            int value = (await _objectAccess.ReadAsync(index, Cia402ObjectIndex.ValueSubIndex, ct)).AsInt32();
            if (value > 0)
                return;

            await _objectAccess.WriteAsync(
                index,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(defaultValue),
                ct);
        }

        private async Task EnsurePositiveUInt16Async(ushort index, ushort defaultValue, CancellationToken ct)
        {
            ushort value = (await _objectAccess.ReadAsync(index, Cia402ObjectIndex.ValueSubIndex, ct)).AsUInt16();
            if (value > 0)
                return;

            await _objectAccess.WriteAsync(
                index,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromUInt16(defaultValue),
                ct);
        }

        private async Task<sbyte> ValidateMotionPreconditionsAsync(
            string commandLabel,
            CancellationToken ct,
            params Cia402OperationMode[] allowedModes)
        {
            ushort statuswordRaw = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.Statusword,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsUInt16();
            Cia402Statusword statusword = new(statuswordRaw);

            ushort errorCode = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ErrorCode,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsUInt16();

            if (statusword.Fault || errorCode != 0)
            {
                throw new InvalidOperationException(
                    $"Drive reports an error before {commandLabel}: Statusword=0x{statuswordRaw:X4} ({statusword.ToDisplayText()}), " +
                    $"0x603F=0x{errorCode:X4} ({DescribeErobErrorCode(errorCode)}). Clear the device error first.");
            }

            if (!statusword.OperationEnabled)
            {
                throw new InvalidOperationException(
                    $"Drive is not Operation Enabled before {commandLabel}. Statusword=0x{statuswordRaw:X4} ({statusword.ToDisplayText()}). Run Shutdown -> Switch On -> Enable Operation first.");
            }

            if (statusword.InternalLimitActive)
            {
                throw new InvalidOperationException(
                    $"Drive internal limit is active before {commandLabel}. Statusword=0x{statuswordRaw:X4}; check position/velocity/current/torque limits and mechanical load.");
            }

            sbyte modeDisplay = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ModesOfOperationDisplay,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsSByte();

            if (!allowedModes.Any(mode => modeDisplay == (sbyte)mode))
            {
                string allowed = string.Join(" or ", allowedModes.Select(mode => $"{mode}({(sbyte)mode})"));
                throw new InvalidOperationException(
                    $"Drive is not in the required mode for {commandLabel}. 0x6061 ModesOfOperationDisplay={modeDisplay}; set mode to {allowed} before sending the command.");
            }

            return modeDisplay;
        }

        private async Task<sbyte> ValidateVelocityPreconditionsAsync(CancellationToken ct)
        {
            ushort statuswordRaw = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.Statusword,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsUInt16();
            Cia402Statusword statusword = new(statuswordRaw);

            ushort errorCode = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ErrorCode,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsUInt16();

            if (statusword.Fault || errorCode != 0)
            {
                throw new InvalidOperationException(
                    $"Drive reports an error before velocity command: Statusword=0x{statuswordRaw:X4} ({statusword.ToDisplayText()}), " +
                    $"0x603F=0x{errorCode:X4} ({DescribeErobErrorCode(errorCode)}). Clear the device error first.");
            }

            if (!statusword.OperationEnabled)
            {
                throw new InvalidOperationException(
                    $"Drive is not Operation Enabled. Statusword=0x{statuswordRaw:X4} ({statusword.ToDisplayText()}). Run Shutdown -> Switch On -> Enable Operation first.");
            }

            if (statusword.InternalLimitActive)
            {
                throw new InvalidOperationException(
                    $"Drive internal limit is active before velocity command. Statusword=0x{statuswordRaw:X4}; chapter 7 points to limits such as max velocity, current/torque, load, or power constraints.");
            }

            return (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ModesOfOperationDisplay,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsSByte();
        }

        private async Task ValidateProfileVelocityRampAsync(CancellationToken ct)
        {
            int acceleration = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ProfileAcceleration,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsInt32();
            int deceleration = (await _objectAccess.ReadAsync(
                Cia402ObjectIndex.ProfileDeceleration,
                Cia402ObjectIndex.ValueSubIndex,
                ct)).AsInt32();

            if (acceleration <= 0 || deceleration <= 0)
            {
                throw new InvalidOperationException(
                    $"Profile Velocity ramp is invalid: 0x6083 ProfileAcceleration={acceleration}, 0x6084 ProfileDeceleration={deceleration}. Set both to a positive value before sending TargetVelocity.");
            }
        }

        public async Task SetTargetTorqueAsync(short targetTorque, CancellationToken ct)  
        {
            Cia402OperationMode? mode = _lastRequestedMode;
            if (mode is null)
            {
                sbyte modeDisplay = (await _objectAccess.ReadAsync(
                    Cia402ObjectIndex.ModesOfOperationDisplay,
                    Cia402ObjectIndex.ValueSubIndex,
                    ct)).AsSByte();
                mode = Enum.IsDefined(typeof(Cia402OperationMode), modeDisplay)
                    ? (Cia402OperationMode)modeDisplay
                    : null;
            }

            if (mode == Cia402OperationMode.CyclicSynchronousTorque)
            {
                await WriteTargetTorquePdoAsync(targetTorque, ct);
                return;
            }

            await ValidateMotionPreconditionsAsync(
                "Profile Torque command",
                ct,
                Cia402OperationMode.ProfileTorque);
            await EnsurePositiveUInt16Async(Cia402ObjectIndex.MaxTorque, DefaultMaxTorque, ct);
            await WriteTargetTorqueAsync(targetTorque, ct);
        }

        public async Task WriteCyclicProcessDataAsync(
            int? targetPosition,
            int? targetVelocity,
            short? targetTorque,
            CancellationToken ct)
        {
            if (_processData is null)
                throw new InvalidOperationException("Cyclic command requires PDO process data, but no process-data provider is active.");

            await _processData.WriteAsync(new ProcessCommand(
                TargetPosition: targetPosition,
                TargetVelocity: targetVelocity,
                TargetTorque: targetTorque), ct);
        }

        private async Task WriteTargetVelocityAsync(int targetVelocity, CancellationToken ct)
        {
            await _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetVelocity,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(targetVelocity),
                ct);

            await TryWriteProcessDataAsync(new ProcessCommand(TargetVelocity: targetVelocity), ct);
        }

        private async Task WriteTargetTorqueAsync(short targetTorque, CancellationToken ct)
        {
            await WriteTargetTorqueSdoAsync(targetTorque, ct);
            await TryWriteProcessDataAsync(new ProcessCommand(TargetTorque: targetTorque), ct);
        }

        private async Task WriteTargetTorquePdoAsync(short targetTorque, CancellationToken ct)
        {
            if (_processData is null)
                throw new InvalidOperationException("CST torque command requires PDO process data, but no process-data provider is active.");

            await _processData.WriteAsync(new ProcessCommand(TargetTorque: targetTorque), ct);
        }

        private Task WriteTargetTorqueSdoAsync(short targetTorque, CancellationToken ct)
        {
            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetTorque,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt16(targetTorque),
                ct);
        }

        private async Task TryWriteProcessDataAsync(ProcessCommand command, CancellationToken ct)
        {
            if (_processData is null)
                return;

            try
            {
                await _processData.WriteAsync(command, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not mapped", StringComparison.OrdinalIgnoreCase))
            {
                // The manual allows these objects through SDO; some factory PDO maps expose
                // only the subset needed by the selected profile.
            }
        }

        private static string DescribeErobErrorCode(ushort code) => code switch
        {
            0x0000 => "Ready to switch on / no device error",
            0x2214 => "Motor current is over current; check current loop parameters, current limit, load, and motor power wiring",
            0x2250 => "Sum of motor three-phase current exceeds the limit; check current loop, three-phase current limit, load, and motor wiring",
            0x2341 => "U phase over current; check current loop, phase current limit, load, and cabling",
            0x2342 => "V phase over current; check current loop, phase current limit, load, and cabling",
            0x2343 => "W phase over current; check current loop, phase current limit, load, and cabling",
            0x3210 => "Bus voltage is overvoltage; check power supply, voltage limit, regenerative load, or braking",
            0x3220 => "Bus voltage is undervoltage; check supply voltage and power capacity",
            0x4110 => "Power component temperature is too high; check heat dissipation, temperature limit, ambient temperature, and load",
            0x7121 => "Blocked motor rotation; check mechanical load, stall settings, continuous current, and electrical angle compensation",
            0x7405 => "The data read from the single-turn encoder at the load side is incorrect; check the power supple's operational status adn measure the voltage to determine if it is statble; update to the latest firm",
            0x7306 => "The data read from the single-turn encoder at the motor side is incorrect; check the power supple's operational status adn measure the voltage to determine if it is statble; update to the latest firm",
            0x730D => "Battery warning error; check cables, replace correct battery, reset battery correctly adn check ther zero point of the devices",
            0x730F => "Battery low voltage; replace, reset battery and confirm the zero point of the device",
            0x7311 => "The position error of the sampled motor end exceeds the limit; check power supply ex: operational status, rapid, firm",
            0x7314 => "Detection of multiple revolutions count battery reconnection. Reset the load end encoder to clear this alarm",
            0x7315 => "The sampling load position error exceeds the upper limit; check status of power supply and measure whether the voltage is stable",
            0x7374 => "Multi-turn position error; reset device by refering to and confirm the zero point of the device",
            0x7377 => "Error message for reset pin detected; check status of power supply and messure whether the voltage is stable",
            0x737A => "Error in startup of single-turn encoder at the load side; check power supply's operational status and measure whether the voltage is stable; update to the latest firmware",
            0x737E => "Error in startup of single-turn encoder at the motor side; check power supply's operational status and measure voltage to determine if it is stable; update to the latest firmware",
            0x8130 => "CAN heartbeat error; check heartbeat timing and CAN connection",
            0x8400 => "Velocity error exceeds limit; check velocity loop, max velocity, current/torque limits, target velocity, load, and supply power",
            0x8401 => "Motor velocity exceeds limit; check encoder configuration, velocity loop, and max velocity setting",
            0x8500 => "Position error exceeds limit; check loop tuning, max velocity, current/torque limits, target command, load, and supply power",
            0xA000 => "EtherCAT communication abnormal; check EtherCAT cable, master OP state, and realtime cycle stability",
            0xF004 => "EtherCAT initialization error; check drive firmware, EtherCAT XML, and EtherCAT hardware",
            0xF005 => "STO function is activated; switch off STO input before clearing the error",
            0xF006 => "Multi-turn circle count error; power-cycle fully or update firmware per eRob guidance",
            0xF008 => "Bus voltage is below the minimum allowable supply voltage 19V; check supply voltage drop and power capacity",
            _ => "Unknown eRob device error; see manual chapter 7, object 0x603F"
        };

        public async Task<DriveSnapshot> ReadSnapshotAsync(CancellationToken ct)
        {
            ProcessSnapshot snapshot;
            if (_processData is not null)
            {
                snapshot = await _processData.ReadAsync(ct);
                snapshot = await FillMissingProcessSnapshotFieldsAsync(snapshot, ct);
            }
            else
            {
                snapshot = new ProcessSnapshot(
                    Statusword: (await _objectAccess.ReadAsync(Cia402ObjectIndex.Statusword, Cia402ObjectIndex.ValueSubIndex, ct)).AsUInt16(),
                    OperationModeDisplay: (await _objectAccess.ReadAsync(Cia402ObjectIndex.ModesOfOperationDisplay, Cia402ObjectIndex.ValueSubIndex, ct)).AsSByte(),
                    PositionActualValue: (await _objectAccess.ReadAsync(Cia402ObjectIndex.PositionActualValue, Cia402ObjectIndex.ValueSubIndex, ct)).AsInt32(),
                    VelocityActualValue: (await _objectAccess.ReadAsync(Cia402ObjectIndex.VelocityActualValue, Cia402ObjectIndex.ValueSubIndex, ct)).AsInt32(),
                    TorqueActualValue: (await _objectAccess.ReadAsync(Cia402ObjectIndex.TorqueActualValue, Cia402ObjectIndex.ValueSubIndex, ct)).AsInt16(),
                    ErrorCode: (await _objectAccess.ReadAsync(Cia402ObjectIndex.ErrorCode, Cia402ObjectIndex.ValueSubIndex, ct)).AsUInt16());
            }

            Cia402Statusword statusword = new(snapshot.Statusword);

            return new DriveSnapshot(
                Statusword: snapshot.Statusword,
                Position: snapshot.PositionActualValue,
                Velocity: snapshot.VelocityActualValue,
                Torque: snapshot.TorqueActualValue,
                // eRob's current CANopen/EtherCAT PDO maps do not expose temperature.
                Temperature: 0,
                ErrorCode: snapshot.ErrorCode,
                StatusText: statusword.ToDisplayText());
        }

        private async Task<ProcessSnapshot> FillMissingProcessSnapshotFieldsAsync(
            ProcessSnapshot snapshot,
            CancellationToken ct)
        {
            if (_processDataCapabilities is null)
                return snapshot;

            sbyte operationModeDisplay = snapshot.OperationModeDisplay;
            int velocityActualValue = snapshot.VelocityActualValue;
            short torqueActualValue = snapshot.TorqueActualValue;
            ushort errorCode = snapshot.ErrorCode;

            if (!_processDataCapabilities.HasOperationModeDisplay)
            {
                operationModeDisplay = (await _objectAccess.ReadAsync(
                    Cia402ObjectIndex.ModesOfOperationDisplay,
                    Cia402ObjectIndex.ValueSubIndex,
                    ct)).AsSByte();
            }

            if (!_processDataCapabilities.HasVelocityActualValue)
            {
                velocityActualValue = (await _objectAccess.ReadAsync(
                    Cia402ObjectIndex.VelocityActualValue,
                    Cia402ObjectIndex.ValueSubIndex,
                    ct)).AsInt32();
            }

            if (!_processDataCapabilities.HasTorqueActualValue)
            {
                torqueActualValue = (await _objectAccess.ReadAsync(
                    Cia402ObjectIndex.TorqueActualValue,
                    Cia402ObjectIndex.ValueSubIndex,
                    ct)).AsInt16();
            }

            if (!_processDataCapabilities.HasErrorCode)
            {
                errorCode = (await _objectAccess.ReadAsync(
                    Cia402ObjectIndex.ErrorCode,
                    Cia402ObjectIndex.ValueSubIndex,
                    ct)).AsUInt16();
            }

            return snapshot with
            {
                OperationModeDisplay = operationModeDisplay,
                VelocityActualValue = velocityActualValue,
                TorqueActualValue = torqueActualValue,
                ErrorCode = errorCode
            };
        }
        
        public async Task<Cia402Statusword> ReadStatuswordAsync(CancellationToken ct)
        {
            if (_processData is not null)
            {
                ProcessSnapshot snapshot = await _processData.ReadAsync(ct);
                return new Cia402Statusword(snapshot.Statusword);
            }

            ObjectValue statuswordValue = await _objectAccess.ReadAsync(
                Cia402ObjectIndex.Statusword,
                Cia402ObjectIndex.ValueSubIndex,
                ct);
            return new Cia402Statusword(statuswordValue.AsUInt16());
        }

        private async Task ApplyProfilePositionParametersAsync(
            int? profileVelocity,
            int? profileAcceleration,
            int? profileDeceleration,
            CancellationToken ct)
        {
            if (profileVelocity is int pv)
            {
                if (pv <= 0)
                    throw new ArgumentOutOfRangeException(nameof(profileVelocity), pv, "0x6081 ProfileVelocity must be positive.");
                await _objectAccess.WriteAsync(
                    Cia402ObjectIndex.ProfileVelocity,
                    Cia402ObjectIndex.ValueSubIndex,
                    ObjectValue.FromInt32(pv),
                    ct);
            }

            if (profileAcceleration is int pa)
            {
                if (pa <= 0)
                    throw new ArgumentOutOfRangeException(nameof(profileAcceleration), pa, "0x6083 ProfileAcceleration must be positive.");
                await _objectAccess.WriteAsync(
                    Cia402ObjectIndex.ProfileAcceleration,
                    Cia402ObjectIndex.ValueSubIndex,
                    ObjectValue.FromInt32(pa),
                    ct);
            }

            if (profileDeceleration is int pd)
            {
                if (pd <= 0)
                    throw new ArgumentOutOfRangeException(nameof(profileDeceleration), pd, "0x6084 ProfileDeceleration must be positive.");
                await _objectAccess.WriteAsync(
                    Cia402ObjectIndex.ProfileDeceleration,
                    Cia402ObjectIndex.ValueSubIndex,
                    ObjectValue.FromInt32(pd),
                    ct);
            }
        }

        private async Task WriteTargetPositionProfileAsync(int targetPosition, CancellationToken ct)
        {
            await _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetPosition,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(targetPosition),
                ct);

            await TryWriteProcessDataAsync(new ProcessCommand(TargetPosition: targetPosition), ct);
        }

        private async Task WriteTargetPositionPdoAsync(int targetPosition, CancellationToken ct)
        {
            if (_processData is not null)
            {
                await _processData.WriteAsync(new ProcessCommand(TargetPosition: targetPosition), ct);
                return;
            }

            throw new InvalidOperationException("CSP position command requires PDO process data, but no process-data provider is active.");
        }

        private async Task WriteControlwordAsync(ushort controlword, CancellationToken ct)
        {
            await _objectAccess.WriteAsync(
                Cia402ObjectIndex.Controlword,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromUInt16(controlword),
                ct);

            if (_processData is not null)
            {
                try
                {
                    await _processData.WriteAsync(new ProcessCommand(Controlword: controlword), ct);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not mapped", StringComparison.OrdinalIgnoreCase))
                {
                    // Some factory EtherCAT PDO layouts do not expose 0x6040 in RPDO.
                    // The state-machine command has already been sent through SDO above.
                }
            }
        }

        private async Task TryRestoreEnableOperationAsync()
        {
            try
            {
                await WriteControlwordAsync(Cia402ControlwordBuilder.EnableOperation, CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup: we don't want trigger-bit restoration to hide the original error.
            }
        }
    }
}
