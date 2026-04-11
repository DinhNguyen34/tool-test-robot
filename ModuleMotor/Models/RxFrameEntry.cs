using System.CodeDom;

namespace ModuleMotor.Models
{
    public class RxFrameEntry
    {
        public DateTime Timestamp { get; init; }
        public byte     Channel   { get; init; }
        public byte     Status    { get; init; }
        public byte[]   Data      { get; init; } = Array.Empty<byte>();

        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string StatusText => Status == 0 ? "OK" : $"ERR 0x{Status:X2}";

        public uint? DecodedRawId
        {
            get
            {
                if (Data.Length < 4)
                    return null;

                // VCanPLib stores the received CAN ID little-endian in data[0..3]
                uint rawId = ((uint)Data[3] << 24)
                           | ((uint)Data[2] << 16)
                           | ((uint)Data[1] << 8)
                           |  Data[0];
                return rawId;
            }
        }

        public byte[] PayloadData => Data.Length > 4
            ? Data.Skip(5).ToArray()
            : Array.Empty<byte>();

        public byte? MotorId
        {
            get
            {
                if (DecodedRawId is not uint rawId)
                    return null;

                RsMotorControl.DecodeCanId(rawId, out var motorId, out _, out _);
                return motorId;
            }
        }

        public byte? CommMode
        {
            get
            {
                if (DecodedRawId is not uint rawId)
                    return null;

                RsMotorControl.DecodeCanId(rawId, out _, out var commMode, out _);
                return commMode;
            }
        }

        public byte? OperationMode
        {
            get
            {
                if (DecodedRawId is not uint rawId)
                    return null;

                RsMotorControl.DecodeCanId(rawId, out _, out _, out var operationMode);
                return operationMode;
            }
        }

        public string CanIdText
        {
            get
            {
                if (DecodedRawId is not uint rawId)
                    return "--";

                RsMotorControl.DecodeCanId(rawId, out var motorId, out var commMode, out var operationMode);
                return $"ID=0x{motorId:X2} CM={commMode} OP={operationMode}";
            }
        }

        public string DataHex => PayloadData.Length > 0
            ? string.Join(" ", PayloadData.Select(b => b.ToString("X2")))
            : "(empty)";
        public string DataLight => Data.Length > 0
            ? string.Join(" ", Data.Select(b => b.ToString("X2")))
            : "(empty)";
    }
}
