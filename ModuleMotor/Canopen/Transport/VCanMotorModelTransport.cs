using ModuleMotor.Models;
using VCanPLib;

namespace ModuleMotor.Canopen.Transport
{
    public sealed class VCanMotorModelTransport : IVCanFrameTransport
    {
        private readonly MotorModel _motorModel;
        private readonly TimeSpan _pollInterval;

        public VCanMotorModelTransport(MotorModel motorModel, TimeSpan? pollInterval = null)
        {
            _motorModel = motorModel;
            _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5);
        }

        public VCanReadCursor CaptureCursor()
        {
            double lastTimestamp = _motorModel
                .GetCanMessages()
                .Select(m => m.time)
                .DefaultIfEmpty(-1)
                .Max();

            return new VCanReadCursor(lastTimestamp);
        }

        public Task SendAsync(CanFrameSpec frame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!_motorModel.SendFrame(frame, out string message))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Failed to send CAN frame." : message);

            return Task.CompletedTask;
        }

        public async Task<VCanReceivedFrame> WaitForFrameAsync(
            Func<VCanReceivedFrame, bool> predicate,
            VCanReadCursor after,
            TimeSpan timeout,
            CancellationToken ct)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                IReadOnlyList<RawDataCan> messages = _motorModel.GetCanMessages();
                foreach (RawDataCan raw in messages)
                {
                    if (!TryParse(raw, out VCanReceivedFrame frame))
                        continue;

                    if (frame.Timestamp <= after.Timestamp)
                        continue;

                    string key = $"{frame.Timestamp:R}|{frame.CanId:X8}|{Convert.ToHexString(frame.Payload)}";
                    if (!seen.Add(key))
                        continue;

                    if (predicate(frame))
                        return frame;
                }

                await Task.Delay(_pollInterval, ct);
            }

            throw new TimeoutException($"Timed out after {timeout.TotalMilliseconds:F0} ms waiting for CAN frame.");
        }

        private static bool TryParse(RawDataCan raw, out VCanReceivedFrame frame)
        {
            frame = default;

            if (raw.status != 0 || raw.data == null || raw.data.Length < 5)
                return false;

            uint canId = ((uint)raw.data[3] << 24)
                       | ((uint)raw.data[2] << 16)
                       | ((uint)raw.data[1] << 8)
                       | raw.data[0];

            int payloadLength = Math.Min(raw.data[4], Math.Max(0, raw.data.Length - 5));
            byte[] payload = new byte[payloadLength];
            Array.Copy(raw.data, 5, payload, 0, payloadLength);

            frame = new VCanReceivedFrame(
                CanId: canId,
                Payload: payload,
                IsExtendedId: canId > 0x7FF,
                Timestamp: raw.time,
                Channel: raw.ch);
            return true;
        }
    }
}
