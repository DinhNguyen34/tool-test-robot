using ModuleTestLed.Models;
using System.Globalization;
using System.Windows;

namespace ModuleTestLed.Views
{
    public partial class LedConfigWindow : Window
    {
        private readonly LedConfig _config;

        public LedConfigWindow(LedConfig config)
        {
            InitializeComponent();
            _config = config;

            TxtCmdAll.Text = $"0x{config.CmdControlAll:X2}";
            TxtCmdLed.Text = $"0x{config.CmdControlLed:X2}";
            TxtMaxPorts.Text = config.MaxPorts.ToString();
            TxtMaxLeds.Text = config.MaxLedsPerPort.ToString();
            TxtMaxRgbw.Text = config.MaxRgbwValue.ToString();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (TryParseHex(TxtCmdAll.Text, out uint cmdAll))
                _config.CmdControlAll = cmdAll;

            if (TryParseHex(TxtCmdLed.Text, out uint cmdLed))
                _config.CmdControlLed = cmdLed;

            if (int.TryParse(TxtMaxPorts.Text, out int maxPorts) && maxPorts > 0)
                _config.MaxPorts = maxPorts;

            if (int.TryParse(TxtMaxLeds.Text, out int maxLeds) && maxLeds > 0 && maxLeds <= 80)
                _config.MaxLedsPerPort = maxLeds;

            if (byte.TryParse(TxtMaxRgbw.Text, out byte maxRgbw))
                _config.MaxRgbwValue = maxRgbw;

            _config.Save();
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static bool TryParseHex(string text, out uint result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }
    }
}
