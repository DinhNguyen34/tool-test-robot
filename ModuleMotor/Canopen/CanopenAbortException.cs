using System.Buffers.Binary;

namespace ModuleMotor.Canopen
{
    public sealed class CanopenAbortException : InvalidOperationException
    {
        public CanopenAbortException(uint abortCode, ushort index, byte subIndex)
            : base($"CANopen SDO abort 0x{abortCode:X8} for 0x{index:X4}:{subIndex:X2}.")
        {
            AbortCode = abortCode;
            Index = index;
            SubIndex = subIndex;
        }

        public uint AbortCode { get; }
        public ushort Index { get; }
        public byte SubIndex { get; }

        public static CanopenAbortException FromPayload(byte[] payload)
        {
            ushort index = payload.Length >= 3
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1, 2))
                : (ushort)0;
            byte subIndex = payload.Length >= 4 ? payload[3] : (byte)0;
            uint abortCode = payload.Length >= 8
                ? BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4))
                : 0;
            return new CanopenAbortException(abortCode, index, subIndex);
        }
    }
}
