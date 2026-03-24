namespace ModuleNetwork.Models
{
    public class UdpLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;  // "TX" or "RX"
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public override string ToString()
            => $"[{Timestamp}] [{Direction}] {Source}  {Message}";
    }
}
