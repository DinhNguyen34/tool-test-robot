using ModuleMotor.Cia402.Models;
using ModuleMotor.Models;

namespace ModuleMotor.Controllers
{
    public sealed class EncosController : IDriveController
    {
        private readonly MotorModel _model;
        private readonly MotorConfig _config;

        public EncosController(MotorModel model, MotorConfig config)
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
                StatusText: $"ENCOS controller scaffold for motor {_config.MotorId}."));
        }

        public Task ExecuteAsync(DriveCommand command, CancellationToken ct)
        {
            throw new NotSupportedException(
                $"ENCOS command dispatch has not been migrated to {nameof(IDriveController)} yet.");
        }
    }
}
