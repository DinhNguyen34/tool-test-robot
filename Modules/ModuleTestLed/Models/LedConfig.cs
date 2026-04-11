using System.IO;
using System.Text.Json;

namespace ModuleTestLed.Models
{
    public class LedConfig
    {
        public uint MessageId { get; set; } = 0x01;
        public uint CmdControlAll { get; set; } = 0x00;
        public uint CmdControlLed { get; set; } = 0x01;
        public int MaxPorts { get; set; } = 7;
        public List<int> LedsPerPort { get; set; } = [80, 80, 80, 80, 80, 80, 80];
        public byte MaxRgbwValue { get; set; } = 255;

        public int GetLedsForPort(int port)
        {
            if (port >= 0 && port < LedsPerPort.Count)
                return LedsPerPort[port];
            return 80;
        }

        public void EnsureLedsPerPortSize()
        {
            LedsPerPort ??= [];
            while (LedsPerPort.Count < MaxPorts)
                LedsPerPort.Add(80);
            if (LedsPerPort.Count > MaxPorts)
                LedsPerPort.RemoveRange(MaxPorts, LedsPerPort.Count - MaxPorts);
        }

        private static readonly string ConfigFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "LedConfig.json");

        public void Save()
        {
            EnsureLedsPerPortSize();
            if (!Directory.Exists(ConfigFolder))
                Directory.CreateDirectory(ConfigFolder);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        public static LedConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<LedConfig>(json) ?? new LedConfig();
                    config.EnsureLedsPerPortSize();
                    return config;
                }
            }
            catch
            {
                // ignore
            }
            var defaultConfig = new LedConfig();
            defaultConfig.EnsureLedsPerPortSize();
            return defaultConfig;
        }
    }
}
