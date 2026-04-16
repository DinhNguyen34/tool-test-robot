using System.Buffers.Binary;

namespace ModuleMotor.Cia402.Abstractions
{
    public readonly record struct ObjectValue(byte[] Raw)
    {
        public static ObjectValue FromByte(byte value)
            => new([value]);

        public static ObjectValue FromSByte(sbyte value)
            => new([(byte)value]);

        public static ObjectValue FromUInt16(ushort value)
        {
            byte[] raw = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(raw, value);
            return new ObjectValue(raw);
        }

        public static ObjectValue FromInt16(short value)
        {
            byte[] raw = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(raw, value);
            return new ObjectValue(raw);
        }

        public static ObjectValue FromInt32(int value)
        {
            byte[] raw = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(raw, value);
            return new ObjectValue(raw);
        }

        public byte AsByte()
            => Raw.Length > 0 ? Raw[0] : (byte)0;

        public sbyte AsSByte()
            => unchecked((sbyte)AsByte());

        public ushort AsUInt16()
            => Raw.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Raw) : (ushort)0;

        public short AsInt16()
            => Raw.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(Raw) : (short)0;

        public int AsInt32()
            => Raw.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(Raw) : 0;
    }
}
