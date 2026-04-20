using Microsoft.Win32;
using ModuleTestBms.Models;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ModuleTestBms.Views
{
    public partial class AscViewerWindow : Window
    {
        private readonly TestBmsModel _model;
        private readonly List<AscLogEntry> _allEntries = [];
        private readonly List<AscLogEntry> _filteredEntries = [];
        private int _timeMode; // 0 = Absolute, 1 = Delta T, 2 = Real Time
        private DateTime? _measurementStartTime;

        public AscViewerWindow(TestBmsModel model)
        {
            InitializeComponent();
            _model = model;
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "ASC Files (*.asc)|*.asc|All Files (*.*)|*.*",
                Title = "Open ASC Log File"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                TxtStatus.Text = $"Loading: {dlg.FileName}";
                LoadAscFile(dlg.FileName);
                TxtStatus.Text = $"Loaded: {Path.GetFileName(dlg.FileName)} — {_allEntries.Count} messages";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ASC file:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Load failed.";
            }
        }

        private void LoadAscFile(string filePath)
        {
            _allEntries.Clear();
            _measurementStartTime = null;
            var lastTimePerMsg = new Dictionary<uint, double>();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                // Skip ASC header/footer lines
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("date", StringComparison.OrdinalIgnoreCase))
                {
                    TryParseDateHeader(trimmed);
                    continue;
                }
                if (trimmed.StartsWith("base", StringComparison.OrdinalIgnoreCase)) continue;
                if (trimmed.StartsWith("internal", StringComparison.OrdinalIgnoreCase)) continue;
                if (trimmed.StartsWith("Begin", StringComparison.OrdinalIgnoreCase)) continue;
                if (trimmed.StartsWith("End", StringComparison.OrdinalIgnoreCase)) continue;
                if (trimmed.Contains("Start of measurement", StringComparison.OrdinalIgnoreCase)) continue;

                var entry = ParseAscLine(trimmed);
                if (entry == null) continue;

                // Calculate delta time per message ID
                if (lastTimePerMsg.TryGetValue(entry.MessageId, out double prevTime))
                {
                    entry.DeltaTime = (entry.AbsoluteTime - prevTime) * 1000.0; // ms
                    if (entry.DeltaTime < 0) entry.DeltaTime = 0;
                }
                lastTimePerMsg[entry.MessageId] = entry.AbsoluteTime;

                // Decode signals from database
                DecodeSignals(entry);

                _allEntries.Add(entry);
            }

            PopulateMsgIdFilter();
            ApplyFilter();
        }

        private AscLogEntry? ParseAscLine(string line)
        {
            // Vector ASC message line format:
            //    0.001234 1  18Fx        Rx   d 8 AA BB CC DD EE FF 00 11  MessageName :: sig=val
            // Split by whitespace
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) return null;

            // parts[0] = timestamp, parts[1] = channel, parts[2] = msg ID, parts[3] = direction
            // parts[4] = 'd' (data frame marker), parts[5] = DLC, parts[6..6+DLC] = data bytes

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double timestamp))
                return null;

            string idStr = parts[2];
            // Remove trailing 'x' for extended IDs
            bool extended = idStr.EndsWith('x') || idStr.EndsWith('X');
            if (extended) idStr = idStr[..^1];

            if (!uint.TryParse(idStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint msgId))
                return null;

            string direction = parts[3];

            // parts[4] should be 'd' for data frame
            if (!parts[4].Equals("d", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!int.TryParse(parts[5], out int dlc))
                return null;

            // Extract data bytes
            int dataStart = 6;
            int availableBytes = Math.Min(dlc, parts.Length - dataStart);
            var payload = new byte[availableBytes];
            var dataHexParts = new List<string>();
            for (int i = 0; i < availableBytes; i++)
            {
                if (byte.TryParse(parts[dataStart + i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    payload[i] = b;
                    dataHexParts.Add(parts[dataStart + i]);
                }
            }

            // Resolve message name from database
            string messageName = GetMessageName(msgId);

            return new AscLogEntry
            {
                AbsoluteTime = timestamp,
                DisplayTime = timestamp.ToString("F6"),
                MessageId = msgId,
                Direction = direction,
                MessageName = messageName,
                Dlc = dlc,
                DataHex = string.Join(" ", dataHexParts),
                Payload = payload
            };
        }

        private string GetMessageName(uint id)
        {
            if (_model.CanDb != null && _model.CanDb.Lookup.TryGetValue(id, out var def))
                return def.MessageName;
            return $"Unknown (0x{id:X3})";
        }

        private void DecodeSignals(AscLogEntry entry)
        {
            if (_model.CanDb == null || !_model.CanDb.Lookup.TryGetValue(entry.MessageId, out var msgDef))
                return;

            foreach (var sigDef in msgDef.Signals)
            {
                ulong rawVal = TestBmsModel.ExtractSignalValue(entry.Payload, sigDef.StartBit, sigDef.Length,
                    sigDef.ByteOrder.Equals("Intel", StringComparison.OrdinalIgnoreCase));

                double physical;
                if (sigDef.DataType.Equals("Signed", StringComparison.OrdinalIgnoreCase))
                {
                    long signedRaw = TestBmsModel.ToSigned(rawVal, sigDef.Length);
                    physical = signedRaw * sigDef.Factor + sigDef.Offset;
                }
                else
                {
                    physical = rawVal * sigDef.Factor + sigDef.Offset;
                }

                entry.Signals.Add(new CanSignalItem
                {
                    SignalName = sigDef.SignalName,
                    RawValue = rawVal.ToString("X"),
                    PhysicalValue = physical,
                    Unit = sigDef.Unit,
                    Description = sigDef.Description
                });
            }
        }

        private void PopulateMsgIdFilter()
        {
            CmbMsgIdFilter.Items.Clear();
            var allItem = new MsgIdFilterItem { MsgId = null, Display = "All" };
            CmbMsgIdFilter.Items.Add(allItem);

            var uniqueIds = _allEntries.Select(e => e.MessageId).Distinct().OrderBy(id => id);
            foreach (var id in uniqueIds)
            {
                string name = GetMessageName(id);
                CmbMsgIdFilter.Items.Add(new MsgIdFilterItem
                {
                    MsgId = id,
                    Display = $"0x{id:X3} - {name}"
                });
            }

            CmbMsgIdFilter.SelectedIndex = 0;
        }

        private void ApplyFilter()
        {
            _filteredEntries.Clear();

            uint? filterMsgId = null;
            if (CmbMsgIdFilter.SelectedItem is MsgIdFilterItem filterItem)
                filterMsgId = filterItem.MsgId;

            foreach (var entry in _allEntries)
            {
                if (filterMsgId.HasValue && entry.MessageId != filterMsgId.Value)
                    continue;

                // Update display time based on mode
                entry.DisplayTime = _timeMode switch
                {
                    1 => entry.DeltaTime > 0 ? $"{entry.DeltaTime:F3} ms" : "-",
                    2 => FormatRealTime(entry.AbsoluteTime),
                    _ => entry.AbsoluteTime.ToString("F6")
                };

                _filteredEntries.Add(entry);
            }

            DgMessages.ItemsSource = null;
            DgMessages.ItemsSource = _filteredEntries;
            TxtCount.Text = $"{_filteredEntries.Count} messages";
        }

        private void CmbTimeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTimeMode.SelectedIndex < 0) return;
            _timeMode = CmbTimeMode.SelectedIndex;
            if (_allEntries.Count > 0)
                ApplyFilter();
        }

        private void CmbMsgIdFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allEntries.Count > 0)
                ApplyFilter();
        }

        private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in _filteredEntries)
                entry.IsExpanded = false;
        }

        private void TryParseDateHeader(string line)
        {
            // ASC date header: "date ddd MMM dd hh:mm:ss.fff tt yyyy"
            if (line.Length <= 5) return;
            string dateStr = line[5..].Trim();

            string[] formats =
            [
                "ddd MMM dd hh:mm:ss.fff tt yyyy",
                "ddd MMM dd HH:mm:ss.fff yyyy",
                "ddd MMM  d hh:mm:ss.fff tt yyyy",
                "ddd MMM  d HH:mm:ss.fff yyyy"
            ];

            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                _measurementStartTime = dt;
            }
        }

        private string FormatRealTime(double absoluteSeconds)
        {
            if (_measurementStartTime.HasValue)
            {
                var realTime = _measurementStartTime.Value.AddSeconds(absoluteSeconds);
                return realTime.ToString("HH:mm:ss.fff");
            }
            return absoluteSeconds.ToString("F6");
        }

        private void DgMessages_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree from the click target to find the DataGridRow
            if (e.OriginalSource is not DependencyObject source) return;

            DataGridRow? row = null;
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is DataGridRow dgRow)
                {
                    row = dgRow;
                    break;
                }
                // Stop if we reach the DataGrid header area
                if (current is DataGridColumnHeader) return;
                current = VisualTreeHelper.GetParent(current);
            }

            if (row?.DataContext is AscLogEntry entry)
                entry.IsExpanded = !entry.IsExpanded;
        }
    }
}
