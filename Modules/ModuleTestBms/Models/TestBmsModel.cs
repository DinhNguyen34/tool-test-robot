using Common.Core.Helpers;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using VCanPLib;

namespace ModuleTestBms.Models
{
    public class TestBmsModel : BindableBase
    {
        private static bool _isVCanLoggerInitialized;
        private readonly VCANPCtrl _canCtrl = new();

        private CanDevice? _selectedCan;
        private bool _isConnected;
        private bool _isRunning;
        private bool _isMonitoring;
        private string _selectedBaudrate = "500";
        private CanDatabase? _canDatabase;
        private CancellationTokenSource? _monitorCts;
        private Thread? _monitorThread;

        public ObservableCollection<CanDevice> ListCanDevices { get; } = [];
        public ObservableCollection<BmsTestCaseItem> TestCases { get; } = [];
        public ObservableCollection<CanMonitorItem> MonitorItems { get; } = [];
        public ObservableCollection<string> BaudrateOptions { get; } =
        [
            "100", "125", "250", "500", "800", "1000"
        ];

        /// <summary>Lookup: message ID → monitor row</summary>
        private readonly Dictionary<uint, CanMonitorItem> _monitorLookup = [];

        public CanDevice? SelectedCan
        {
            get => _selectedCan;
            set => SetProperty(ref _selectedCan, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
        }

        public string SelectedBaudrate
        {
            get => _selectedBaudrate;
            set => SetProperty(ref _selectedBaudrate, value);
        }

        public CanDatabase? CanDb
        {
            get => _canDatabase;
            set => SetProperty(ref _canDatabase, value);
        }

        public TestBmsModel()
        {
            EnsureVCanLoggerInitialized();
            CanDb = CanDatabase.LoadFromJson();
            BuildTestCases();
        }

        private static void EnsureVCanLoggerInitialized()
        {
            if (_isVCanLoggerInitialized) return;
            VCanPLib.LogHelper.Init();
            _isVCanLoggerInitialized = true;
        }

        #region CAN Connection

        public void RefreshCanDevices()
        {
            ListCanDevices.Clear();
            var list = _canCtrl.GetAllCanAvailable();
            if (list == null || list.Count == 0)
            {
                SelectedCan = null;
                return;
            }
            foreach (var d in list)
                ListCanDevices.Add(d);
            if (SelectedCan == null || !ListCanDevices.Contains(SelectedCan))
                SelectedCan = ListCanDevices[0];
        }

        public bool Connect(out string message)
        {
            if (SelectedCan == null)
            {
                message = "No CAN device selected.";
                return false;
            }

            if (!int.TryParse(SelectedBaudrate, out int baud))
                baud = 500;

            var canBaud = MapBaudrate(baud);
            string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logFolder);
            string rawLogPath = Path.Combine(logFolder, "BmsCanRaw.txt");
            bool ok = _canCtrl.Connect(SelectedCan, rawLogPath, canBaud, canBaud, bitrateSwitch.SW_ON, CanType.CAN_FD);
            SelectedCan.IsConnected = ok;
            IsConnected = ok;

            if (ok)
            {
                _canCtrl.EnableReadLog(true);
                message = $"Connected to {SelectedCan.DisplayName} at {baud}k.";
            }
            else
            {
                message = $"Failed to connect to {SelectedCan.DisplayName}.";
            }
            return ok;
        }

        public void Disconnect()
        {
            StopMonitor();
            if (_canCtrl.GetOpenStatus())
                _canCtrl.Close();
            if (SelectedCan != null)
                SelectedCan.IsConnected = false;
            IsConnected = false;
        }

        #endregion

        #region Monitor

        public void StartMonitor()
        {
            if (IsMonitoring) return;
            IsMonitoring = true;
            _monitorCts = new CancellationTokenSource();
            _monitorThread = new Thread(() => MonitorLoop(_monitorCts.Token))
            {
                IsBackground = true,
                Name = "BmsMonitorThread"
            };
            _monitorThread.Start();
        }

        public void StopMonitor()
        {
            if (!IsMonitoring) return;
            IsMonitoring = false;
            _monitorCts?.Cancel();
            _monitorThread?.Join(2000);
            _monitorCts?.Dispose();
            _monitorCts = null;
        }

        public void ClearMonitor()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MonitorItems.Clear();
                _monitorLookup.Clear();
            });
        }

        private void MonitorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var messages = _canCtrl.GetCanMessegers();
                    if (messages != null && messages.Count > 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var raw in messages)
                            {
                                if (ct.IsCancellationRequested) break;
                                ProcessRawCanMessage(raw);
                            }
                        });
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Common.Core.Helpers.LogHelper.Exception(ex); }
            }
        }

        private void ProcessRawCanMessage(RawDataCan raw)
        {
            if (raw.data == null || raw.data.Length < 5) return;

            // data layout from VCANPCtrl: [ID(4 bytes), Len(1), Data..., padding(3), CRC(1)]
            uint msgId = BitConverter.ToUInt32(raw.data, 0);
            byte dataLen = raw.data[4];
            int available = raw.data.Length - 5;
            if (dataLen > available) dataLen = (byte)available;
            byte[] payload = new byte[dataLen];
            Array.Copy(raw.data, 5, payload, 0, dataLen);
            double timestamp = raw.time;

            string dataHex = string.Join(" ", payload.Select(b => b.ToString("X2")));

            // Get or create monitor row
            if (!_monitorLookup.TryGetValue(msgId, out var item))
            {
                item = new CanMonitorItem
                {
                    MessageId = msgId,
                    MessageName = GetMessageName(msgId),
                    LastTimestamp = timestamp
                };
                _monitorLookup[msgId] = item;
                MonitorItems.Add(item);
            }

            // Calculate cyclic (ms)
            if (item.LastTimestamp > 0 && timestamp > item.LastTimestamp)
            {
                double cyclicMs = (timestamp - item.LastTimestamp) * 1000.0;
                item.Cyclic = $"{cyclicMs:F1} ms";
            }
            item.LastTimestamp = timestamp;
            item.DataLen = dataLen;
            item.DataHex = dataHex;

            // Decode signals if database is loaded
            DecodeSignals(item, msgId, payload);
        }

        private string GetMessageName(uint id)
        {
            if (CanDb != null && CanDb.Lookup.TryGetValue(id, out var def))
                return def.MessageName;
            return $"Unknown (0x{id:X3})";
        }

        private void DecodeSignals(CanMonitorItem item, uint msgId, byte[] payload)
        {
            if (CanDb == null || !CanDb.Lookup.TryGetValue(msgId, out var msgDef))
                return;

            // Ensure signal items exist
            if (item.Signals.Count == 0)
            {
                foreach (var sigDef in msgDef.Signals)
                {
                    item.Signals.Add(new CanSignalItem
                    {
                        SignalName = sigDef.SignalName,
                        Unit = sigDef.Unit,
                        Description = sigDef.Description
                    });
                }
            }

            // Decode each signal
            for (int i = 0; i < msgDef.Signals.Count && i < item.Signals.Count; i++)
            {
                var sigDef = msgDef.Signals[i];
                var sigItem = item.Signals[i];

                ulong rawVal = ExtractSignalValue(payload, sigDef.StartBit, sigDef.Length,
                    sigDef.ByteOrder.Equals("Intel", StringComparison.OrdinalIgnoreCase));

                double physical;
                if (sigDef.DataType.Equals("Signed", StringComparison.OrdinalIgnoreCase))
                {
                    long signedRaw = ToSigned(rawVal, sigDef.Length);
                    physical = signedRaw * sigDef.Factor + sigDef.Offset;
                }
                else
                {
                    physical = rawVal * sigDef.Factor + sigDef.Offset;
                }

                sigItem.RawValue = rawVal.ToString("X");
                sigItem.PhysicalValue = physical;
                sigItem.NotifyDisplayTextChanged();
            }
        }

        /// <summary>
        /// Extract a signal value from a CAN payload given start bit, length, and byte order.
        /// </summary>
        private static ulong ExtractSignalValue(byte[] data, int startBit, int bitLength, bool isLittleEndian)
        {
            ulong value = 0;

            if (isLittleEndian)
            {
                // Intel byte order: LSB at startBit
                for (int i = 0; i < bitLength; i++)
                {
                    int bitPos = startBit + i;
                    int byteIdx = bitPos / 8;
                    int bitIdx = bitPos % 8;
                    if (byteIdx < data.Length)
                    {
                        if ((data[byteIdx] & (1 << bitIdx)) != 0)
                            value |= (1UL << i);
                    }
                }
            }
            else
            {
                // Motorola byte order: MSB at startBit
                for (int i = 0; i < bitLength; i++)
                {
                    int srcBit = startBit - i;
                    if (srcBit < 0)
                    {
                        // wrap around rows (Motorola bit numbering)
                        int row = (startBit / 8) + 1 + (-srcBit - 1) / 8;
                        int col = 7 - ((-srcBit - 1) % 8);
                        srcBit = row * 8 + col;
                    }
                    int byteIdx = srcBit / 8;
                    int bitIdx = srcBit % 8;
                    if (byteIdx < data.Length)
                    {
                        if ((data[byteIdx] & (1 << bitIdx)) != 0)
                            value |= (1UL << (bitLength - 1 - i));
                    }
                }
            }

            return value;
        }

        private static long ToSigned(ulong raw, int bitLength)
        {
            if (bitLength >= 64) return (long)raw;
            ulong mask = 1UL << (bitLength - 1);
            if ((raw & mask) != 0)
                return (long)(raw | (~0UL << bitLength));
            return (long)raw;
        }

        #endregion

        #region Test Cases

        private void BuildTestCases()
        {
            TestCases.Clear();
            // Placeholder test cases — will be completed later
            TestCases.Add(new BmsTestCaseItem { No = 1, Name = "TC1 - BMS Communication", Description = "Verify BMS CAN communication" });
            TestCases.Add(new BmsTestCaseItem { No = 2, Name = "TC2 - Voltage Reading", Description = "Verify BMS cell voltage reading" });
            TestCases.Add(new BmsTestCaseItem { No = 3, Name = "TC3 - Temperature Reading", Description = "Verify BMS temperature sensor reading" });
            TestCases.Add(new BmsTestCaseItem { No = 4, Name = "TC4 - State Machine", Description = "Verify BMS state transitions" });
        }

        public async Task RunSelectedTestCases(Action<string> log)
        {
            IsRunning = true;
            foreach (var tc in TestCases.Where(t => t.IsSelected))
            {
                if (!IsRunning) break;
                tc.Status = BmsTestStatus.Running;
                var sw = Stopwatch.StartNew();
                log($"Running {tc.Name}...");

                // Placeholder: test logic will be added later
                await Task.Delay(500);
                sw.Stop();
                tc.Duration = $"{sw.ElapsedMilliseconds} ms";
                tc.Status = BmsTestStatus.HasRun;
                log($"{tc.Name} completed in {tc.Duration}.");
            }
            IsRunning = false;
        }

        #endregion

        #region Database

        public string ImportDatabase(string csvPath, Action<string>? log = null)
        {
            log?.Invoke($"Importing CSV: {csvPath}");
            CanDb = CanDatabase.ImportFromCsv(csvPath);
            string jsonPath = CanDb.SaveToJson();
            log?.Invoke($"Database converted: {CanDb.Messages.Count} messages, saved to {jsonPath}");
            return jsonPath;
        }

        #endregion

        #region Report

        public string SaveTestReport()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== BMS Test Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                sb.AppendLine();
                sb.AppendLine($"{"No",-5} {"Test Case",-30} {"Status",-10} {"Duration",-12} {"Remark"}");
                sb.AppendLine(new string('-', 90));

                foreach (var tc in TestCases)
                {
                    sb.AppendLine($"{tc.No,-5} {tc.Name,-30} {tc.StatusText,-10} {tc.Duration,-12} {tc.Remark}");
                }

                string reportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Report");
                if (!Directory.Exists(reportFolder))
                    Directory.CreateDirectory(reportFolder);

                string fileName = $"BmsTestReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(reportFolder, fileName);
                File.WriteAllText(filePath, sb.ToString());
                Common.Core.Helpers.LogHelper.Debug($"Report saved: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Common.Core.Helpers.LogHelper.Exception(ex);
                return string.Empty;
            }
        }

        #endregion

        #region Helpers

        private static CanBaudrate MapBaudrate(int baud) => baud switch
        {
            100 => CanBaudrate.BAUDRATE_100,
            125 => CanBaudrate.BAUDRATE_120,
            250 => CanBaudrate.BAUDRATE_250,
            500 => CanBaudrate.BAUDRATE_500,
            800 => CanBaudrate.BAUDRATE_800,
            1000 => CanBaudrate.BAUDRATE_1000,
            _ => CanBaudrate.BAUDRATE_500
        };

        #endregion
    }
}
