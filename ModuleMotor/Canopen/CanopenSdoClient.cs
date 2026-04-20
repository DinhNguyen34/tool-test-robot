using System.Buffers.Binary;
using ModuleMotor.Canopen.Transport;

namespace ModuleMotor.Canopen
{
    public sealed class CanopenSdoClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

        private readonly IVCanFrameTransport _transport;

        public CanopenSdoClient(IVCanFrameTransport transport)
        {
            _transport = transport;
        }

        public async Task<byte[]> ReadAsync(
            byte nodeId,
            ushort index,
            byte subIndex,
            CancellationToken ct,
            TimeSpan? timeout = null)
        {
            VCanReadCursor cursor = _transport.CaptureCursor();
            await _transport.SendAsync(CanopenFrameBuilder.BuildSdoReadRequest(nodeId, index, subIndex), ct);

            VCanReceivedFrame response = await _transport.WaitForFrameAsync(
                frame => frame.CanId == (uint)(0x580 + nodeId),
                cursor,
                timeout ?? DefaultTimeout,
                ct);

            return DecodeReadResponse(response.Payload, index, subIndex);
        }

        public async Task WriteAsync(
            byte nodeId,
            ushort index,
            byte subIndex,
            byte[] value,
            CancellationToken ct,
            TimeSpan? timeout = null)
        {
            VCanReadCursor cursor = _transport.CaptureCursor();
            await _transport.SendAsync(CanopenFrameBuilder.BuildSdoWriteRequest(nodeId, index, subIndex, value), ct);

            VCanReceivedFrame response = await _transport.WaitForFrameAsync(
                frame => frame.CanId == (uint)(0x580 + nodeId),
                cursor,
                timeout ?? DefaultTimeout,
                ct);

            DecodeWriteResponse(response.Payload, index, subIndex);
        }

        private static byte[] DecodeReadResponse(byte[] payload, ushort index, byte subIndex)
        {
            ValidatePayload(payload, index, subIndex);

            if (payload[0] == 0x80)
                throw CanopenAbortException.FromPayload(payload);

            return payload[0] switch
            {
                0x4F => [payload[4]],
                0x4B => payload.Skip(4).Take(2).ToArray(),
                0x47 => payload.Skip(4).Take(3).ToArray(),
                0x43 => payload.Skip(4).Take(4).ToArray(),
                _ => throw new InvalidOperationException(
                    $"Unsupported SDO read response command specifier 0x{payload[0]:X2} for 0x{index:X4}:{subIndex:X2}.")
            };
        }

        private static void DecodeWriteResponse(byte[] payload, ushort index, byte subIndex)
        {
            ValidatePayload(payload, index, subIndex);

            if (payload[0] == 0x80)
                throw CanopenAbortException.FromPayload(payload);

            if (payload[0] != 0x60)
            {
                throw new InvalidOperationException(
                    $"Unexpected SDO write response command specifier 0x{payload[0]:X2} for 0x{index:X4}:{subIndex:X2}.");
            }
        }

        private static void ValidatePayload(byte[] payload, ushort index, byte subIndex)
        {
            if (payload.Length < 4)
                throw new InvalidOperationException("SDO response payload is shorter than 4 bytes.");

            ushort responseIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1, 2));
            byte responseSubIndex = payload[3];

            if (responseIndex != index || responseSubIndex != subIndex)
            {
                throw new InvalidOperationException(
                    $"Mismatched SDO response. Expected 0x{index:X4}:{subIndex:X2}, got 0x{responseIndex:X4}:{responseSubIndex:X2}.");
            }
        }
    }
}
