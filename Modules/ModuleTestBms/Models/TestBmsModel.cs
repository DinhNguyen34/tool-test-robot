using Common.Core.Helpers;
using Microsoft.Win32;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
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
        private bool _isLogging;
        private StreamWriter? _logWriter;
        private string _logFilePath = string.Empty;
        private DateTime _loggingStartTime;
        private double _firstMessageTime;
        private readonly object _logLock = new();
        public List<MessageExcelDefinition> Messages { get; set; }
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

        public bool IsLogging
        {
            get => _isLogging;
            set => SetProperty(ref _isLogging, value);
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
                StartMonitor();
                message = $"Connected to {SelectedCan.DisplayName} at {baud}k. Monitor started.";
            }
            else
            {
                message = $"Failed to connect to {SelectedCan.DisplayName}.";
            }
            return ok;
        }

        public void Disconnect()
        {
            if (IsLogging) StopLogging();
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
                        // Write to log file from background thread
                        if (_isLogging)
                        {
                            lock (_logLock)
                            {
                                foreach (var raw in messages)
                                    WriteLogLine(raw);
                            }
                        }

                        // Update monitor UI on dispatcher thread
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
        internal static ulong ExtractSignalValue(byte[] data, int startBit, int bitLength, bool isLittleEndian)
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

        internal static long ToSigned(ulong raw, int bitLength)
        {
            if (bitLength >= 64) return (long)raw;
            ulong mask = 1UL << (bitLength - 1);
            if ((raw & mask) != 0)
                return (long)(raw | (~0UL << bitLength));
            return (long)raw;
        }

        #endregion

        #region Logging

        public void StartLogging(string filePath)
        {
            if (IsLogging) return;
            _logFilePath = filePath;
            _loggingStartTime = DateTime.Now;
            _firstMessageTime = -1;
            lock (_logLock)
            {
                _logWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            }
            IsLogging = true;
        }

        public string StopLogging()
        {
            if (!IsLogging) return string.Empty;
            IsLogging = false;

            lock (_logLock)
            {
                _logWriter?.Flush();
                _logWriter?.Close();
                _logWriter = null;
            }

            // Convert TXT log to Vector ASC format
            string ascPath = Path.ChangeExtension(_logFilePath, ".asc");
            try
            {
                ConvertToAsc(_logFilePath, ascPath);
            }
            catch (Exception ex)
            {
                Common.Core.Helpers.LogHelper.Exception(ex);
                return string.Empty;
            }

            return ascPath;
        }

        private void WriteLogLine(RawDataCan raw)
        {
            if (_logWriter == null || raw.data == null || raw.data.Length < 5) return;

            uint msgId = BitConverter.ToUInt32(raw.data, 0);
            byte dataLen = raw.data[4];
            int available = raw.data.Length - 5;
            if (dataLen > available) dataLen = (byte)available;
            byte[] payload = new byte[dataLen];
            Array.Copy(raw.data, 5, payload, 0, dataLen);

            if (_firstMessageTime < 0)
                _firstMessageTime = raw.time;

            double relTime = raw.time - _firstMessageTime;
            if (relTime < 0) relTime = 0;

            string direction = raw.status == 0 ? "Rx" : "Tx";
            string dataHex = string.Join(" ", payload.Select(b => b.ToString("X2")));

            _logWriter.WriteLine($"{relTime:F6}\t1\t{msgId:X}\t{direction}\t{dataLen}\t{dataHex}");
        }

        private void ConvertToAsc(string txtPath, string ascPath)
        {
            var lines = File.ReadAllLines(txtPath);
            using var writer = new StreamWriter(ascPath, false, Encoding.UTF8);

            string dateStr = _loggingStartTime.ToString("ddd MMM dd hh:mm:ss.fff tt yyyy",
                System.Globalization.CultureInfo.InvariantCulture);

            writer.WriteLine($"date {dateStr}");
            writer.WriteLine("base hex  timestamps absolute");
            writer.WriteLine("internal events logged");
            writer.WriteLine($"Begin Triggerblock {dateStr}");
            writer.WriteLine("   0.000000 Start of measurement");

            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 6) continue;

                string timestamp = parts[0];
                string channel = parts[1];
                string msgIdHex = parts[2];
                string direction = parts[3];
                string dlc = parts[4];
                string dataHex = parts[5];

                uint msgId = Convert.ToUInt32(msgIdHex, 16);
                // Extended ID (29-bit) appends 'x'
                string idStr = msgId > 0x7FF ? $"{msgIdHex}x" : msgIdHex;

                var sb = new StringBuilder();
                sb.Append($"   {timestamp} {channel}  {idStr,-16} {direction}   d {dlc} {dataHex}");

                // Append message name and decoded signals from database
                if (CanDb != null && CanDb.Lookup.TryGetValue(msgId, out var msgDef))
                {
                    sb.Append($"  {msgDef.MessageName}");

                    if (msgDef.Signals.Count > 0 && !string.IsNullOrWhiteSpace(dataHex))
                    {
                        byte[] payload = dataHex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(h => Convert.ToByte(h, 16)).ToArray();

                        var sigParts = new List<string>();
                        foreach (var sigDef in msgDef.Signals)
                        {
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

                            string unit = string.IsNullOrEmpty(sigDef.Unit) ? "" : $" [{sigDef.Unit}]";
                            sigParts.Add($"{sigDef.SignalName} = {physical:G}{unit}");
                        }

                        sb.Append(" :: ");
                        sb.Append(string.Join("; ", sigParts));
                    }
                }

                writer.WriteLine(sb.ToString());
            }

            writer.WriteLine("End TriggerBlock");
        }

        public ICommand OpenConvertCSVCommand { get; }

        public void ConvertDataToCsv(string txtPath, string csvPath, List<uint> selectedIds)
        {
            var lines = File.ReadLines(txtPath);
            using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);

            writer.WriteLine("Timestamp,Message,Signal,PhysicalValue,Unit");

            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 6) continue;

                if (!uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint msgId))
                    continue;

                // Filter ID
                if (selectedIds != null && selectedIds.Count > 0 && !selectedIds.Contains(msgId))
                    continue;

                if (CanDb != null && CanDb.Lookup.TryGetValue(msgId, out var msgDef))
                {
                    string timestamp = parts[0];
                    string dataHex = parts[5];

                    if (msgDef.Signals.Count > 0 && !string.IsNullOrWhiteSpace(dataHex))
                    {
                        byte[] payload = dataHex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(h => Convert.ToByte(h, 16)).ToArray();

                        foreach (var sigDef in msgDef.Signals)
                        {
                            ulong rawVal = ExtractSignalValue(payload, sigDef.StartBit, sigDef.Length,
                                sigDef.ByteOrder.Equals("Intel", StringComparison.OrdinalIgnoreCase));

                            double physical = (sigDef.DataType.Equals("Signed", StringComparison.OrdinalIgnoreCase))
                                ? ToSigned(rawVal, sigDef.Length) * sigDef.Factor + sigDef.Offset
                                : rawVal * sigDef.Factor + sigDef.Offset;

                            writer.WriteLine($"{timestamp},{msgDef.MessageName},{sigDef.SignalName},{physical:G6},{sigDef.Unit}");
                        }
                    }
                }
            }
        }

        public List<uint> GetUniqueIdsFromLog(string filePath)
        {
            var uniqueIds = new HashSet<uint>();
            try
            {
                var lines = File.ReadLines(filePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 3 && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint id))
                    {
                        uniqueIds.Add(id);
                    }
                }
            }
            catch (Exception ex) { /* Log error */ }
            return uniqueIds.ToList();
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
