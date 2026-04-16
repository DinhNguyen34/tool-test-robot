using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Core;
using ModuleMotor.Cia402.Models;
using ModuleMotor.Controllers;

namespace ModuleMotor.Cia402
{
    public sealed class Cia402Controller : IDriveController
    {
        private readonly Cia402DriveService _driveService;

        public Cia402Controller(
            ICia402ObjectAccess objectAccess,
            ICia402ProcessData? processData = null)
        {
            _driveService = new Cia402DriveService(objectAccess, processData);
        }

        public Task ConnectAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public Task<DriveSnapshot> ReadSnapshotAsync(CancellationToken ct)
        {
            return _driveService.ReadSnapshotAsync(ct);
        }

        public Task ExecuteAsync(DriveCommand command, CancellationToken ct)
        {
            return command switch
            {
                FaultResetDriveCommand => _driveService.FaultResetAsync(ct),
                ShutdownDriveCommand => _driveService.ShutdownAsync(ct),
                SwitchOnDriveCommand => _driveService.SwitchOnAsync(ct),
                EnableOperationDriveCommand => _driveService.EnableOperationAsync(ct),
                DisableOperationDriveCommand => _driveService.DisableOperationAsync(ct),
                QuickStopDriveCommand => _driveService.QuickStopAsync(ct),
                SetModeDriveCommand modeCommand => _driveService.SetModeAsync(modeCommand.Mode, ct),
                SyncActualPositionToTargetDriveCommand => _driveService.SyncActualPositionToTargetAsync(ct),
                MoveAbsolutePositionDriveCommand positionCommand => _driveService.MoveAbsolutePositionAsync(
                    positionCommand.TargetPosition,
                    positionCommand.CommandMode,
                    positionCommand.ImmediateChange,
                    ct),
                SetVelocityDriveCommand velocityCommand => _driveService.SetTargetVelocityAsync(velocityCommand.TargetVelocity, ct),
                SetTorqueDriveCommand torqueCommand => _driveService.SetTargetTorqueAsync(torqueCommand.TargetTorque, ct),
                _ => throw new NotSupportedException($"Unsupported drive command type: {command.GetType().Name}.")
            };
        }
    }
}
