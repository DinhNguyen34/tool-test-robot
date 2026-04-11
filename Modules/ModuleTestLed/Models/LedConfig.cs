using System.IO;
using System.Text.Json;

namespace ModuleTestLed.Models
{
    public class LedConfig
    {
        public uint CmdControlAll { get; set; } = 0x00;
        public uint CmdControlLed { get; set; } = 0x01;
        public int MaxPorts { get; set; } = 7;
        public int MaxLedsPerPort { get; set; } = 80;
        public byte MaxRgbwValue { get; set; } = 255;

        private static readonly string ConfigFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "LedConfig.json");

        public void Save()
        {
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
                    return JsonSerializer.Deserialize<LedConfig>(json) ?? new LedConfig();
                }
            }
            catch
            {
                // ignore
            }
            return new LedConfig();
        }
    }
}
