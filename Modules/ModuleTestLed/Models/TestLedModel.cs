using Common.Core.Helpers;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using VCanPLib;

namespace ModuleTestLed.Models
{
    public class TestLedModel : BindableBase
    {
        private static bool _isVCanLoggerInitialized;
        private readonly VCANPCtrl _canCtrl = new();

        private CanDevice? _selectedCan;
        private bool _isConnected;
        private bool _isRunning;
        private string _selectedBaudrate = "500";
        private LedConfig _config;

        public ObservableCollection<CanDevice> ListCanDevices { get; } = new();
        public ObservableCollection<LedTestCaseItem> TestCases { get; } = new();
        public ObservableCollection<string> BaudrateOptions { get; } = new()
        {
            "100", "125", "250", "500", "800", "1000"
        };

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

        public string SelectedBaudrate
        {
            get => _selectedBaudrate;
            set => SetProperty(ref _selectedBaudrate, value);
        }

        public LedConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public TestLedModel()
        {
            EnsureVCanLoggerInitialized();
            _config = LedConfig.Load();
            BuildTestCases();
        }

        private static void EnsureVCanLoggerInitialized()
        {
            if (_isVCanLoggerInitialized) return;
            VCanPLib.LogHelper.Init();
            _isVCanLoggerInitialized = true;
        }

        public void RefreshCanDevices()
        {
            string? previousDisplayName = SelectedCan?.DisplayName;
            ListCanDevices.Clear();
            var list = _canCtrl.GetAllCanAvailable();
            if (list == null || list.Count == 0)
            {
                SelectedCan = null;
                return;
            }
            foreach (var d in list)
                ListCanDevices.Add(d);

            if (!string.IsNullOrWhiteSpace(previousDisplayName))
            {
                SelectedCan = ListCanDevices.FirstOrDefault(d => d.DisplayName == previousDisplayName);
            }

            if (SelectedCan == null)
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
            string rawLogPath = Path.Combine(logFolder, "LedCanRaw.txt");
            bool ok = _canCtrl.Connect(SelectedCan, rawLogPath, canBaud, canBaud, bitrateSwitch.SW_OFF,  CanType.CAN_FD);
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
            if (_canCtrl.GetOpenStatus())
                _canCtrl.Close();
            if (SelectedCan != null)
                SelectedCan.IsConnected = false;
            IsConnected = false;
        }

        #region CAN Send

        /// <summary>
        /// CAN FD max data = 64 bytes. New format: [CMD(1), len(1), port(1), addr...(N), R, G, B, W(4)].
        /// Max LED addresses per single CAN FD message = 64 - 2 - 1 - 4 = 57.
        /// </summary>
        private const int MaxLedsPerCanFdMessage = 57;

        /// <summary>
        /// Valid CAN FD data lengths (DLC). Lengths 0-8 are always valid;
        /// above 8 only 12, 16, 20, 24, 32, 48, 64 are accepted.
        /// </summary>
        private static int NextValidCanFdDlc(int length)
        {
            if (length <= 8) return length;
            ReadOnlySpan<int> valid = [12, 16, 20, 24, 32, 48, 64];
            foreach (int v in valid)
            {
                if (length <= v) return v;
            }
            return 64;
        }

        private bool SendCanMessage(uint canId, byte[] data)
        {
            if (!_canCtrl.GetOpenStatus()) return false;

            // Pad to next valid CAN FD DLC if needed (extra bytes are 0x00)
            int paddedLen = NextValidCanFdDlc(data.Length);
            if (paddedLen > data.Length)
                Array.Resize(ref data, paddedLen);

            string hexId = $"0x{canId:X}";
            return _canCtrl.SendMessage(hexId, data, false);        // Send can fd message with standart ID 
        }

        /// <summary>
        /// CMD Control All: control all LEDs of a port.
        /// Data = [CMD(0x00), len, port, R, G, B, W]
        /// </summary>
        private bool SendControlAll(byte port, byte r, byte g, byte b, byte w)
        {
            byte[] payload = [port, r, g, b, w];
            byte[] data = new byte[2 + payload.Length];
            data[0] = (byte)Config.CmdControlAll;
            data[1] = (byte)payload.Length;
            Array.Copy(payload, 0, data, 2, payload.Length);
            return SendCanMessage(Config.MessageId, data);
        }

        /// <summary>
        /// CMD Control Led: control specific LEDs on a port.
        /// Data = [CMD(0x01), len, port, addr1..addrN, R, G, B, W]
        /// Automatically splits into multiple CAN FD messages (max 57 addresses each)
        /// with 100ms delay between messages when ledAddresses.Length > 57.
        /// </summary>
        private bool SendControlLed(byte port, byte[] ledAddresses, byte r, byte g, byte b, byte w)
        {
            if (ledAddresses.Length <= MaxLedsPerCanFdMessage)
            {
                return SendControlLedSingle(port, ledAddresses, r, g, b, w);
            }

            bool allOk = true;
            for (int i = 0; i < ledAddresses.Length; i += MaxLedsPerCanFdMessage)
            {
                int count = Math.Min(MaxLedsPerCanFdMessage, ledAddresses.Length - i);
                byte[] chunk = new byte[count];
                Array.Copy(ledAddresses, i, chunk, 0, count);
                if (!SendControlLedSingle(port, chunk, r, g, b, w))
                    allOk = false;
                if (i + count < ledAddresses.Length)
                    Thread.Sleep(100);
            }
            return allOk;
        }

        private bool SendControlLedSingle(byte port, byte[] ledAddresses, byte r, byte g, byte b, byte w)
        {
            byte[] payload = new byte[1 + ledAddresses.Length + 4];
            payload[0] = port;
            Array.Copy(ledAddresses, 0, payload, 1, ledAddresses.Length);
            int offset = 1 + ledAddresses.Length;
            payload[offset] = r;
            payload[offset + 1] = g;
            payload[offset + 2] = b;
            payload[offset + 3] = w;

            byte[] data = new byte[2 + payload.Length];
            data[0] = (byte)Config.CmdControlLed;
            data[1] = (byte)payload.Length;
            Array.Copy(payload, 0, data, 2, payload.Length);
            return SendCanMessage(Config.MessageId, data);
        }

        /// <summary>
        /// Public method to send LED command from external callers (e.g., TestOneLedWindow).
        /// Automatically splits into multiple CAN FD messages if needed.
        /// </summary>
        public bool SendLedCommand(byte port, byte[] ledAddresses, byte r, byte g, byte b, byte w)
        {
            return SendControlLed(port, ledAddresses, r, g, b, w);
        }

        /// <summary>
        /// Turn off all LEDs on all ports.
        /// </summary>
        private void TurnOffAll()
        {
            for (byte port = 1; port <= Config.MaxPorts; port++)
            {
                SendControlAll(port, 0, 0, 0, 0);
                Thread.Sleep(10);
            }
        }

        #endregion

        #region Test case builder

        public void BuildTestCases()
        {
            TestCases.Clear();
            int no = 1;

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC1 - All Ports ON/OFF",
                Description = "Control all LEDs of all ports RGBW=255 for 5000ms, then off."
            });

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC2 - Per-Port Sequential",
                Description = "Control all LEDs per port RGBW=255 every 2000ms, then off."
            });

            for (int p = 1; p <= Config.MaxPorts; p++)
            {
                TestCases.Add(new LedTestCaseItem
                {
                    No = no++,
                    Name = $"TC3-P{p} - RGBW Cycle Port {p}",
                    Description = $"Port {p}: Change cycle R/G/B/W every 2000ms."
                });
            }

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC4 - Random LED Stress",
                Description = "Every 100ms control 10 random LEDs on random port with random color, 50 iterations."
            });

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC5 - Random Line Scan",
                Description = "Scan LEDs one-by-one with one active LED at a time and a new random RGBW color on each step."
            });

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC6 - Boundary Address Test",
                Description = "For each port, verify LED 0, last LED, and the pair [0,last] with different colors."
            });

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC7 - Chunk Split Test",
                Description = "Send 57 LEDs, 58 LEDs, and full-port LED commands to verify CAN FD frame splitting."
            });

            TestCases.Add(new LedTestCaseItem
            {
                No = no++,
                Name = "TC8 - Port Isolation Test",
                Description = "Light different LEDs on two ports with different colors to verify one port does not affect another."
            });
        }

        #endregion

        #region Test case execution

        public async Task RunSelectedTestCases(Action<string> logAction)
        {
            if (!_canCtrl.GetOpenStatus())
            {
                logAction("CAN not connected.");
                return;
            }

            IsRunning = true;

            foreach (var tc in TestCases)
            {
                if (!tc.IsSelected) continue;
                if (!IsRunning) break;

                tc.Status = LedTestStatus.Running;
                tc.Duration = "-";
                tc.Remark = "";
                var sw = Stopwatch.StartNew();

                try
                {
                    logAction($"[START] {tc.Name}");
                    await Task.Run(() => ExecuteTestCase(tc, logAction));
                    sw.Stop();
                    tc.Duration = $"{sw.ElapsedMilliseconds}ms";

                    // Mark as executed, awaiting manual verdict (Pass/Fail)
                    if (tc.Status == LedTestStatus.Running)
                        tc.Status = LedTestStatus.HasRun;

                    logAction($"[END]   {tc.Name} => {tc.StatusText} ({tc.Duration})");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    tc.Status = LedTestStatus.Fail;
                    tc.Duration = $"{sw.ElapsedMilliseconds}ms";
                    tc.Remark = ex.Message;
                    logAction($"[ERROR] {tc.Name}: {ex.Message}");
                }
            }

            IsRunning = false;
            logAction("All selected test cases completed.");
        }

        private void ExecuteTestCase(LedTestCaseItem tc, Action<string> logAction)
        {
            byte maxVal = Config.MaxRgbwValue;

            if (tc.Name.StartsWith("TC1"))
            {
                RunTC1_AllPortsOnOff(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC2"))
            {
                RunTC2_PerPortSequential(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC3"))
            {
                int port = ParsePortFromName(tc.Name);
                RunTC3_RgbwCycle(port, maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC4"))
            {
                RunTC4_RandomStress(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC5"))
            {
                RunTC5_RandomLineScan(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC6"))
            {
                RunTC6_BoundaryAddressTest(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC7"))
            {
                RunTC7_ChunkSplitTest(maxVal, logAction);
            }
            else if (tc.Name.StartsWith("TC8"))
            {
                RunTC8_PortIsolationTest(maxVal, logAction);
            }
        }

        private void RunTC1_AllPortsOnOff(byte maxVal, Action<string> log)
        {
            log("TC1: Turning all LEDs ON (all ports)...");
            for (byte port = 1; port <= Config.MaxPorts; port++)
            {
                SendControlAll(port, maxVal, maxVal, maxVal, maxVal);
                Thread.Sleep(10);
            }

            Thread.Sleep(300000);

            log("TC1: Turning all LEDs OFF...");
            TurnOffAll();
            Thread.Sleep(2000);
        }

        private void RunTC2_PerPortSequential(byte maxVal, Action<string> log)
        {
            for (byte port = 1; port <= Config.MaxPorts; port++)
            {
                log($"TC2: Port {port} ON...");
                SendControlAll(port, maxVal, maxVal, maxVal, maxVal);
                Thread.Sleep(2000);
            }

            log("TC2: Turning all LEDs OFF...");
            TurnOffAll();
            Thread.Sleep(2000);
        }

        private void RunTC3_RgbwCycle(int port, byte maxVal, Action<string> log)
        {
            var colors = new (byte R, byte G, byte B, byte W)[]
            {
                (maxVal, 0, 0, 0),
                (0, maxVal, 0, 0),
                (0, 0, maxVal, 0),
                (0, 0, 0, maxVal),
            };

            foreach (var c in colors)
            {
                log($"TC3: Port {port} => R={c.R} G={c.G} B={c.B} W={c.W}");
                SendControlAll((byte)port, c.R, c.G, c.B, c.W);
                Thread.Sleep(2000);
            }

            log($"TC3: Port {port} OFF...");
            SendControlAll((byte)port, 0, 0, 0, 0);
        }

        private void RunTC4_RandomStress(byte maxVal, Action<string> log)
        {
            var rng = new Random();

            for (int i = 0; i < 100; i++)
            {
                byte port = (byte)rng.Next(1, Config.MaxPorts + 1);
                int maxLeds = Config.GetLedsForPort(port);
                var addresses = Enumerable.Range(0, maxLeds)
                    .OrderBy(_ => rng.Next())
                    .Take(10)
                    .Select(a => (byte)a)
                    .ToArray();

                byte r = (byte)rng.Next(0, maxVal + 1);
                byte g = (byte)rng.Next(0, maxVal + 1);
                byte b = (byte)rng.Next(0, maxVal + 1);
                byte w = (byte)rng.Next(0, maxVal + 1);

                log($"TC4: Iter {i + 1}/50 Port={port} R={r} G={g} B={b} W={w}");
                SendControlLed(port, addresses, r, g, b, w);
                Thread.Sleep(100);
            }

            log("TC4: Turning all LEDs OFF...");
            TurnOffAll();
        }

        private void RunTC5_RandomLineScan(byte maxVal, Action<string> log)
        {
            var rng = new Random();
            byte? previousPort = null;
            byte? previousAddress = null;

            log("TC5: Starting random line scan ...");
            TurnOffAll();
            Thread.Sleep(100);

            for (byte port = 1; port <= Config.MaxPorts; port++)
            {
                int maxLeds = Config.GetLedsForPort(port);
                log($"TC5: Port: {port}, total LEDs = {maxLeds}");

                for (byte address = 0; address < maxLeds; address++)
                {
                    if (!IsRunning)
                    {
                        log("TC5: Stop requested, Turning all LEDs OFF ...");
                        TurnOffAll();
                        return;
                    }
                    if (previousPort is byte oldPort && previousAddress is byte oldAddress)
                    {
                        SendControlLed(oldPort, [oldAddress], 0, 0, 0, 0);
                        Thread.Sleep(50);
                    }
                    byte r = (byte)rng.Next(0, maxVal + 1);
                    byte g = (byte)rng.Next(0, maxVal + 1);
                    byte b = (byte)rng.Next(0, maxVal + 1);
                    byte w = (byte)rng.Next(0, maxVal + 1);

                    SendControlLed(port, [address], r, g, b, w);
                    previousPort = port;
                    previousAddress = address;
                    Thread.Sleep(50);
                }
            }
            
            if (previousPort is byte finalPort && previousAddress is byte finalAddress)
            {
                SendControlLed(finalPort, [finalAddress], 0, 0, 0, 0);
            }

            log("TC5: Random line Scan completed. LEDs OFF.");
            TurnOffAll();
        }

        private void RunTC6_BoundaryAddressTest(byte maxVal, Action<string> log)
        {
            log("TC6: Starting boundary address test...");
            TurnOffAll();
            Thread.Sleep(100);

            for (byte port = 1; port <= Config.MaxPorts; port++)
            {
                if (!IsRunning)
                {
                    log("TC6: Stop requested, turning all LEDs OFF...");
                    TurnOffAll();
                    return;
                }

                int ledCount = Config.GetLedsForPort(port);
                if (ledCount <= 0)
                {
                    log($"TC6: Port {port} has no LEDs configured. Skip.");
                    continue;
                }

                byte first = 0;
                byte last = (byte)(ledCount - 1);

                log($"TC6: Port {port} -> first LED {first} RED");
                SendControlLed(port, [first], maxVal, 0, 0, 0);
                Thread.Sleep(800);
                SendControlLed(port, [first], 0, 0, 0, 0);
                Thread.Sleep(100);

                log($"TC6: Port {port} -> last LED {last} GREEN");
                SendControlLed(port, [last], 0, maxVal, 0, 0);
                Thread.Sleep(800);
                SendControlLed(port, [last], 0, 0, 0, 0);
                Thread.Sleep(100);

                if (last != first)
                {
                    log($"TC6: Port {port} -> pair [{first}, {last}] BLUE");
                    SendControlLed(port, [first, last], 0, 0, maxVal, 0);
                    Thread.Sleep(1000);
                    SendControlLed(port, [first, last], 0, 0, 0, 0);
                    Thread.Sleep(150);
                }
            }

            log("TC6: Boundary address test completed. LEDs OFF.");
            TurnOffAll();
        }

        private void RunTC7_ChunkSplitTest(byte maxVal, Action<string> log)
        {
            byte? port = null;
            int ledCount = 0;

            for (byte candidate = 1; candidate <= Config.MaxPorts; candidate++)
            {
                int count = Config.GetLedsForPort(candidate);
                if (count > MaxLedsPerCanFdMessage)
                {
                    port = candidate;
                    ledCount = count;
                    break;
                }
            }

            if (port is null)
            {
                log($"TC7: No port has more than {MaxLedsPerCanFdMessage} LEDs. Skip chunk split test.");
                return;
            }

            byte selectedPort = port.Value;
            log($"TC7: Using Port {selectedPort} with {ledCount} LEDs.");
            TurnOffAll();
            Thread.Sleep(100);

            if (!IsRunning)
            {
                log("TC7: Stop requested before execution.");
                TurnOffAll();
                return;
            }

            byte[] first57 = BuildSequentialAddresses(MaxLedsPerCanFdMessage);
            log($"TC7: Port {selectedPort} -> first {first57.Length} LEDs RED (single CAN FD frame boundary)");
            SendControlLed(selectedPort, first57, maxVal, 0, 0, 0);
            Thread.Sleep(1200);
            SendControlLed(selectedPort, first57, 0, 0, 0, 0);
            Thread.Sleep(150);

            if (!IsRunning)
            {
                log("TC7: Stop requested after first chunk.");
                TurnOffAll();
                return;
            }

            byte[] first58 = BuildSequentialAddresses(MaxLedsPerCanFdMessage + 1);
            log($"TC7: Port {selectedPort} -> first {first58.Length} LEDs GREEN (must split into multiple CAN FD frames)");
            SendControlLed(selectedPort, first58, 0, maxVal, 0, 0);
            Thread.Sleep(1200);
            SendControlLed(selectedPort, first58, 0, 0, 0, 0);
            Thread.Sleep(150);

            if (!IsRunning)
            {
                log("TC7: Stop requested after split chunk.");
                TurnOffAll();
                return;
            }

            byte[] fullPort = BuildSequentialAddresses(ledCount);
            log($"TC7: Port {selectedPort} -> all {fullPort.Length} LEDs BLUE (full-port split)");
            SendControlLed(selectedPort, fullPort, 0, 0, maxVal, 0);
            Thread.Sleep(1500);
            SendControlLed(selectedPort, fullPort, 0, 0, 0, 0);

            log("TC7: Chunk split test completed. LEDs OFF.");
            TurnOffAll();
        }

        private void RunTC8_PortIsolationTest(byte maxVal, Action<string> log)
        {
            if (Config.MaxPorts < 2)
            {
                log("TC8: Need at least 2 ports for isolation test. Skip.");
                return;
            }

            byte portA = 3;
            byte portB = 4;
            int ledsA = Config.GetLedsForPort(portA);
            int ledsB = Config.GetLedsForPort(portB);

            if (ledsA <= 0 || ledsB <= 0)
            {
                log($"TC8: Port {portA} or Port {portB} has no LEDs configured. Skip.");
                return;
            }

            byte ledA0 = 0;
            byte ledA1 = (byte)Math.Min(1, ledsA - 1);
            byte ledB0 = 0;

            log($"TC8: Port {portA} and Port {portB} isolation test starting...");
            TurnOffAll();
            Thread.Sleep(100);

            if (!IsRunning)
            {
                log("TC8: Stop requested before execution.");
                TurnOffAll();
                return;
            }

            log($"TC8: Port {portA} LED {ledA0} RED");
            SendControlLed(portA, [ledA0], maxVal, 0, 0, 0);
            Thread.Sleep(800);

            if (!IsRunning)
            {
                log("TC8: Stop requested after first port.");
                TurnOffAll();
                return;
            }

            log($"TC8: Port {portB} LED {ledB0} BLUE while Port {portA} LED {ledA0} stays RED");
            SendControlLed(portB, [ledB0], 0, 0, maxVal, 0);
            Thread.Sleep(1000);

            if (!IsRunning)
            {
                log("TC8: Stop requested before port update.");
                TurnOffAll();
                return;
            }

            log($"TC8: Port {portA} switch to LED {ledA1} GREEN, Port {portB} should remain unchanged");
            SendControlLed(portA, [ledA0], 0, 0, 0, 0);
            Thread.Sleep(50);
            SendControlLed(portA, [ledA1], 0, maxVal, 0, 0);
            Thread.Sleep(1000);

            log("TC8: Isolation test completed. LEDs OFF.");
            TurnOffAll();
        }

        private static byte[] BuildSequentialAddresses(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => (byte)index)
                .ToArray();
        }

        private static int ParsePortFromName(string name)
        {
            // e.g. "TC3-P2 - RGBW Cycle Port 2"
            int idx = name.IndexOf("-P");
            if (idx >= 0)
            {
                int spaceIdx = name.IndexOf(' ', idx);
                if (spaceIdx < 0) spaceIdx = name.Length;
                string num = name.Substring(idx + 2, spaceIdx - idx - 2);
                if (int.TryParse(num, out int p)) return p;
            }
            return 1;
        }

        #endregion

        #region Report

        public string SaveTestReport()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== LED Test Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
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

                string fileName = $"LedTestReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
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

        public void ReloadConfig()
        {
            Config = LedConfig.Load();
            BuildTestCases();
        }

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
