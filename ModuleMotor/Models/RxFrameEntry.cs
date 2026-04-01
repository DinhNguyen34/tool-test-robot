namespace ModuleMotor.Models
{
    public class RxFrameEntry
    {
        public DateTime Timestamp { get; init; }
        public byte     Channel   { get; init; }
        public byte     Status    { get; init; }
        public byte[]   Data      { get; init; } = Array.Empty<byte>();

        public string TimeText   => Timestamp.ToString("HH:mm:ss.fff");
        public string DataHex    => Data.Length > 0
            ? string.Join(" ", Data.Select(b => b.ToString("X2")))
            : "(empty)";
        public string StatusText => Status == 0 ? "OK" : $"ERR 0x{Status:X2}";
        public string CanIdText  => Data.Length > 0 ? $"0x{Data[0]:X2}" : "--";
    }
}
