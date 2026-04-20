using ModuleMotor.Cia402.Models;
using ModuleMotor.Models;

namespace ModuleMotor.Controllers
{
    public sealed class RobstrideController : IDriveController
    {
        private readonly MotorModel _model;
        private readonly MotorConfig _config;

        public RobstrideController(MotorModel model, MotorConfig config)
        {
            _model = model;
            _config = config;
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
            return Task.FromResult(new DriveSnapshot(
                Position: 0,
                Velocity: 0,
                Torque: 0,
                Temperature: 0,
                ErrorCode: 0,
                StatusText: $"Robstride controller scaffold for motor {_config.MotorId}."));
        }

        public Task ExecuteAsync(DriveCommand command, CancellationToken ct)
        {
            throw new NotSupportedException(
                $"Robstride command dispatch has not been migrated to {nameof(IDriveController)} yet.");
        }
    }
}
