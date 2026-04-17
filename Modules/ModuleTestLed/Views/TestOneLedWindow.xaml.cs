using ModuleTestLed.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModuleTestLed.Views
{
    public partial class TestOneLedWindow : Window
    {
        private readonly TestLedModel _model;

        public ObservableCollection<PortLedGroup> PortGroups { get; } = new();

        public TestOneLedWindow(TestLedModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = this;
            BuildLedGrid();
            InitSliderMax();
        }

        private void BuildLedGrid()
        {
            PortGroups.Clear();
            var config = _model.Config;
            for (int p = 1; p <= config.MaxPorts; p++)
            {
                var group = new PortLedGroup { PortIndex = p };
                int ledCount = config.GetLedsForPort(p);
                for (int a = 0; a < ledCount; a++)
                {
                    group.Leds.Add(new LedItem { Port = p, Address = a });
                }
                PortGroups.Add(group);
            }
        }

        private void InitSliderMax()
        {
            var max = _model.Config.MaxRgbwValue;
            SliderR.Maximum = max;
            SliderR.Value = max;
            SliderG.Maximum = max;
            SliderG.Value = max;
            SliderB.Maximum = max;
            SliderB.Value = max;
            SliderW.Maximum = max;
            SliderW.Value = max;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in PortGroups)
                group.IsAllSelected = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in PortGroups)
                group.IsAllSelected = false;
        }

        private async void SendCmd_Click(object sender, RoutedEventArgs e)
        {
            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;
            byte w = (byte)SliderW.Value;

            // Collect selections on UI thread
            var selections = new List<(byte Port, byte[] Addresses)>();
            foreach (var group in PortGroups)
            {
                var selectedLeds = group.Leds.Where(l => l.IsSelected).ToArray();
                if (selectedLeds.Length == 0) continue;

                byte port = (byte)group.PortIndex;
                byte[] addresses = selectedLeds.Select(l => (byte)l.Address).ToArray();
                selections.Add((port, addresses));
            }

            if (selections.Count == 0)
            {
                TxtStatus.Text = "No LED selected.";
                return;
            }

            TxtStatus.Text = "Sending...";

            // Send on background thread so Thread.Sleep(20) in SendControlLed
            // does not block the UI message pump / CAN library dispatch
            var (sentCount, failCount) = await Task.Run(() =>
            {
                int sent = 0;
                int fail = 0;
                for (int i = 0; i < selections.Count; i++)
                {
                    var (port, addresses) = selections[i];
                    bool ok = _model.SendLedCommand(port, addresses, r, g, b, w);
                    if (ok) sent += addresses.Length;
                    else fail += addresses.Length;

                    // Delay between ports to avoid CAN bus overload
                    if (i + 1 < selections.Count)
                        Thread.Sleep(100);
                }
                return (sent, fail);
            });

            if (failCount > 0)
            {
                TxtStatus.Text = $"Sent: {sentCount}, Failed: {failCount}. Check CAN connection.";
            }
            else
            {
                TxtStatus.Text = $"Sent RGBW({r},{g},{b},{w}) to {sentCount} LED(s).";
            }
        }
    }
}
