using ModuleTestLed.Models;
using System.Globalization;
using System.Windows;

namespace ModuleTestLed.Views
{
    public class PortLedConfigItem
    {
        public int Port { get; set; }
        public int MaxLeds { get; set; }
    }

    public partial class LedConfigWindow : Window
    {
        private readonly LedConfig _config;
        private List<PortLedConfigItem> _portItems = [];

        public LedConfigWindow(LedConfig config)
        {
            InitializeComponent();
            _config = config;

            TxtMessageId.Text = $"0x{config.MessageId:X2}";
            TxtCmdAll.Text = $"0x{config.CmdControlAll:X2}";
            TxtCmdLed.Text = $"0x{config.CmdControlLed:X2}";
            TxtMaxPorts.Text = config.MaxPorts.ToString();
            TxtMaxRgbw.Text = config.MaxRgbwValue.ToString();

            BuildPortItems(config);
        }

        private void BuildPortItems(LedConfig config)
        {
            _portItems = [];
            for (int i = 0; i < config.MaxPorts; i++)
            {
                _portItems.Add(new PortLedConfigItem
                {
                    Port = i,
                    MaxLeds = config.GetLedsForPort(i)
                });
            }
            DgLedsPerPort.ItemsSource = null;
            DgLedsPerPort.ItemsSource = _portItems;
        }

        private void OnRefreshPorts(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtMaxPorts.Text, out int maxPorts) && maxPorts > 0 && maxPorts <= 20)
            {
                // Rebuild the port items list to match new port count
                var newItems = new List<PortLedConfigItem>();
                for (int i = 0; i < maxPorts; i++)
                {
                    int leds = i < _portItems.Count ? _portItems[i].MaxLeds : 80;
                    newItems.Add(new PortLedConfigItem { Port = i, MaxLeds = leds });
                }
                _portItems = newItems;
                DgLedsPerPort.ItemsSource = null;
                DgLedsPerPort.ItemsSource = _portItems;
            }
            else
            {
                MessageBox.Show("Max Ports must be between 1 and 20.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (TryParseHex(TxtMessageId.Text, out uint msgId))
                _config.MessageId = msgId;

            if (TryParseHex(TxtCmdAll.Text, out uint cmdAll))
                _config.CmdControlAll = cmdAll;

            if (TryParseHex(TxtCmdLed.Text, out uint cmdLed))
                _config.CmdControlLed = cmdLed;

            if (int.TryParse(TxtMaxPorts.Text, out int maxPorts) && maxPorts > 0)
                _config.MaxPorts = maxPorts;

            if (byte.TryParse(TxtMaxRgbw.Text, out byte maxRgbw))
                _config.MaxRgbwValue = maxRgbw;

            // Read per-port LED counts from DataGrid
            _config.LedsPerPort = [];
            foreach (var item in _portItems)
            {
                int leds = Math.Clamp(item.MaxLeds, 1, 255);
                _config.LedsPerPort.Add(leds);
            }

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
