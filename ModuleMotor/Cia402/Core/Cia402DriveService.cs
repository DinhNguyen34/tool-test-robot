using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Models;

namespace ModuleMotor.Cia402.Core
{
    public sealed class Cia402DriveService
    {
        private readonly ICia402ObjectAccess _objectAccess;
        private readonly ICia402ProcessData? _processData;

        public Cia402DriveService(
            ICia402ObjectAccess objectAccess,
            ICia402ProcessData? processData = null)
        {
            _objectAccess = objectAccess;
            _processData = processData;
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

        public Task SetModeAsync(Cia402OperationMode mode, CancellationToken ct)
        {
            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.ModesOfOperation,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromSByte((sbyte)mode),
                ct);
        }

        public async Task SyncActualPositionToTargetAsync(CancellationToken ct)
        {
            ObjectValue actualPosition = await _objectAccess.ReadAsync(
                Cia402ObjectIndex.PositionActualValue,
                Cia402ObjectIndex.ValueSubIndex,
                ct);

            await _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetPosition,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(actualPosition.AsInt32()),
                ct);
        }

        public async Task MoveAbsolutePositionAsync(
            int targetPosition,
            Cia402PositionCommandMode commandMode,
            bool immediateChange,
            CancellationToken ct)
        {
            if (commandMode == Cia402PositionCommandMode.CyclicSynchronousPosition)
            {
                await WriteTargetPositionAsync(targetPosition, ct);
                return;
            }

            await WriteTargetPositionAsync(targetPosition, ct);
            ushort triggerWord = Cia402ControlwordBuilder.BuildProfilePositionTrigger(immediateChange, absoluteMove: true);
            bool triggerWritten = false;

            try
            {
                await WriteControlwordAsync(triggerWord, ct);
                triggerWritten = true;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    Cia402Statusword sw = await ReadStatuswordAsync(ct);
                    if (sw.Fault)
                        throw new InvalidOperationException("Drive entered Fault state during position move");
                    if (sw.SetPointAcknowledge)
                        break;
                    await Task.Delay(5, ct);
                }
            }
            finally
            {
                if (triggerWritten)
                    await TryRestoreEnableOperationAsync();
            }
        }

        public Task SetTargetVelocityAsync(int targetVelocity, CancellationToken ct)
        {
            if(_processData is not null)
            {
                return _processData.WriteAsync(new ProcessCommand(TargetVelocity: targetVelocity), ct).AsTask();
            }
            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetVelocity,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(targetVelocity),
                ct);
        }

        public Task SetTargetTorqueAsync(short targetTorque, CancellationToken ct)  
        {
            if(_processData is not null)
            {
                return _processData.WriteAsync(new ProcessCommand(TargetTorque: targetTorque), ct).AsTask();    
            }
            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetTorque,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt16(targetTorque),
                ct);
        }

        public async Task<DriveSnapshot> ReadSnapshotAsync(CancellationToken ct)
        {
            ProcessSnapshot snapshot;
            if (_processData is not null)
            {
                snapshot = await _processData.ReadAsync(ct);
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
                Position: snapshot.PositionActualValue,
                Velocity: snapshot.VelocityActualValue,
                Torque: snapshot.TorqueActualValue,
                Temperature: 0,
                ErrorCode: snapshot.ErrorCode,
                StatusText: statusword.ToDisplayText());
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

        private Task WriteTargetPositionAsync(int targetPosition, CancellationToken ct)
        {
            if (_processData is not null)
                return _processData.WriteAsync(new ProcessCommand(TargetPosition: targetPosition), ct).AsTask();

            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.TargetPosition,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromInt32(targetPosition),
                ct);
        }

        private Task WriteControlwordAsync(ushort controlword, CancellationToken ct)
        {
            if (_processData is not null)
                return _processData.WriteAsync(new ProcessCommand(Controlword: controlword), ct).AsTask();

            return _objectAccess.WriteAsync(
                Cia402ObjectIndex.Controlword,
                Cia402ObjectIndex.ValueSubIndex,
                ObjectValue.FromUInt16(controlword),
                ct);
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
