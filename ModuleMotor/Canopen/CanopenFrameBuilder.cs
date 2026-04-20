using System.Buffers.Binary;
using ModuleMotor.Models;

namespace ModuleMotor.Canopen
{
    public static class CanopenFrameBuilder
    {
        public static CanFrameSpec BuildNmt(byte command, byte nodeId)
            => new("0x000", [command, nodeId], false);

        /// <summary>
        /// SYNC frame: COB-ID 0x080, DLC 0.
        /// Triggers synchronous TPDO transmission from all listening slaves.
        /// </summary>
        public static CanFrameSpec BuildSync()
            => new("0x080", [], false);

        /// <summary>
        /// RPDO1 frame: COB-ID 0x200 + nodeId (8 bytes).
        /// Layout: Controlword[0-1] | ModesOfOperation[2] | pad[3] | TargetPosition[4-7]
        /// </summary>
        public static CanFrameSpec BuildRpdo1(byte nodeId, byte[] data)
            => new($"0x{(0x200 + nodeId):X3}", data, false);

        /// <summary>
        /// RPDO2 frame: COB-ID 0x300 + nodeId (6 bytes).
        /// Layout: TargetVelocity[0-3] | TargetTorque[4-5]
        /// </summary>
        public static CanFrameSpec BuildRpdo2(byte nodeId, byte[] data)
            => new($"0x{(0x300 + nodeId):X3}", data, false);

        public static CanFrameSpec BuildSdoReadRequest(byte nodeId, ushort index, byte subIndex)
        {
            byte[] payload = new byte[8];
            payload[0] = 0x40;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), index);
            payload[3] = subIndex;
            return new CanFrameSpec($"0x{(0x600 + nodeId):X3}", payload, false);
        }

        public static CanFrameSpec BuildSdoWriteRequest(byte nodeId, ushort index, byte subIndex, byte[] value)
        {
            byte[] payload = new byte[8];
            payload[0] = value.Length switch
            {
                1 => 0x2F,
                2 => 0x2B,
                3 => 0x27,
                4 => 0x23,
                _ => throw new NotSupportedException($"Expedited SDO write supports 1..4 bytes only. Requested {value.Length}.")
            };

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), index);
            payload[3] = subIndex;
            Array.Copy(value, 0, payload, 4, value.Length);
            return new CanFrameSpec($"0x{(0x600 + nodeId):X3}", payload, false);
        }
    }
}
