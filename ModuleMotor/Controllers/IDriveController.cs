using ModuleMotor.Cia402.Models;

namespace ModuleMotor.Controllers
{
    public interface IDriveController
    {
        Task ConnectAsync(CancellationToken ct);
        Task DisconnectAsync();
        Task<DriveSnapshot> ReadSnapshotAsync(CancellationToken ct);
        Task ExecuteAsync(DriveCommand command, CancellationToken ct);
    }
}
