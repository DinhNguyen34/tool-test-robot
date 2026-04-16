using ModuleMotor.Models;

namespace ModuleMotor.Canopen.Transport
{
    public readonly record struct VCanReadCursor(double Timestamp);

    public readonly record struct VCanReceivedFrame(
        uint CanId,
        byte[] Payload,
        bool IsExtendedId,
        double Timestamp,
        byte Channel);

    public interface IVCanFrameTransport
    {
        VCanReadCursor CaptureCursor();
        Task SendAsync(CanFrameSpec frame, CancellationToken ct);
        Task<VCanReceivedFrame> WaitForFrameAsync(
            Func<VCanReceivedFrame, bool> predicate,
            VCanReadCursor after,
            TimeSpan timeout,
            CancellationToken ct);
    }
}
