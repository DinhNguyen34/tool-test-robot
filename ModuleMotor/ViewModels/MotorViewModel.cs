using Common.Core.Helpers;
using Microsoft.Win32;
using ModuleMotor.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace ModuleMotor.ViewModels
{
    public class MotorViewModel : BindableBase
    {
        private const int N_MOTORS = 8;
        private readonly IRegionManager _regionManager;

        // ── Motor channels ─────────────────────────────────────────────────────
        public ObservableCollection<MotorChannelModel> Motors { get; } = new();

        // ── Test cases ─────────────────────────────────────────────────────────
        public List<TestCaseItem> TestCases { get; } = new();

        // ── Config ─────────────────────────────────────────────────────────────
        private MotorConfig _config = new();
        public MotorConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        // ── CAN state ──────────────────────────────────────────────────────────
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                SetProperty(ref _isConnected, value);
                RaisePropertyChanged(nameof(ConnectLabel));
                RaisePropertyChanged(nameof(CanStatusText));
            }
        }
        public string ConnectLabel  => IsConnected ? "Stop"        : "CAN Connect";
        public string CanStatusText => IsConnected ? "CONNECTED"   : "DISCONNECTED";

        // ── Port selection ─────────────────────────────────────────────────────
        public ObservableCollection<string> AvailablePorts { get; } = new();

        public static IReadOnlyList<int> BaudRates { get; } = new[]
            {19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000};

        private string _selectedPort = string.Empty;
        public string SelectedPort
        {
            get => _selectedPort;
            set
            {
                SetProperty(ref _selectedPort, value);
                Config.SerialPort = value;
            }
        }

        private int _selectedBaud = 115200;
        public int SelectedBaud
        {
            get => _selectedBaud;
            set
            {
                SetProperty(ref _selectedBaud, value);
                Config.BaudRate = value;
            }
        }

        // ── Bulk Kp / Kd ──────────────────────────────────────────────────────
        private double _allKp = 20.0;
        public double AllKp
        {
            get => _allKp;
            set => SetProperty(ref _allKp, value);
        }

        private double _allKd = 1.0;
        public double AllKd
        {
            get => _allKd;
            set => SetProperty(ref _allKd, value);
        }

        // ── Log ────────────────────────────────────────────────────────────────
        public ObservableCollection<string> LogEntries { get; } = new();
        private bool _isLogging;
        private StreamWriter? _logWriter;

        private string _logStatusText = "Not logging";
        public string LogStatusText
        {
            get => _logStatusText;
            set => SetProperty(ref _logStatusText, value);
        }

        // ── Commands ───────────────────────────────────────────────────────────
        public DelegateCommand                      CanConnectCommand     { get; }
        public DelegateCommand                      ZeroCmdCommand        { get; }
        public DelegateCommand                      HoldPositionCommand   { get; }
        public DelegateCommand                      SetAllKpCommand       { get; }
        public DelegateCommand                      SetAllKdCommand       { get; }
        public DelegateCommand                      SaveLogCommand        { get; }
        public DelegateCommand                      LoadConfigCommand     { get; }
        public DelegateCommand                      SaveConfigCommand     { get; }
        public DelegateCommand                      ClearLogCommand       { get; }
        public DelegateCommand                      GoBackCommand         { get; }
        public DelegateCommand                      RefreshPortsCommand   { get; }
        public DelegateCommand<MotorChannelModel>   SendMotorCommand      { get; }
        public DelegateCommand                      SendAllMotorsCommand  { get; }

        // ── Constructor ────────────────────────────────────────────────────────
        public MotorViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;

            for (int i = 0; i < N_MOTORS; i++)
                Motors.Add(new MotorChannelModel(i + 1));

            var tcLabels = new[]
            {
                "Get Device ID", "Motor Enabled", "Motor Stop", "Set Zero Mechanical",
                "Max Position",  "Min Position",  "Get State Motor", "Max Speed"
            };
            for (int i = 0; i < N_MOTORS; i++)
            {
                var num   = i + 1;
                var label = tcLabels[i];
                TestCases.Add(new TestCaseItem
                {
                    Number  = num,
                    Label   = label,
                    Command = new DelegateCommand(() => OnRunTestCase(num, label))
                });
            }

            CanConnectCommand    = new DelegateCommand(OnCanConnect);
            ZeroCmdCommand       = new DelegateCommand(OnZeroCmd);
            HoldPositionCommand  = new DelegateCommand(OnHoldPosition);
            SetAllKpCommand      = new DelegateCommand(OnSetAllKp);
            SetAllKdCommand      = new DelegateCommand(OnSetAllKd);
            SaveLogCommand       = new DelegateCommand(OnSaveLog);
            LoadConfigCommand    = new DelegateCommand(OnLoadConfig);
            SaveConfigCommand    = new DelegateCommand(OnSaveConfig);
            ClearLogCommand      = new DelegateCommand(() => LogEntries.Clear());
            GoBackCommand        = new DelegateCommand(OnGoBack);
            RefreshPortsCommand  = new DelegateCommand(RefreshPorts);
            SendMotorCommand     = new DelegateCommand<MotorChannelModel>(OnSendMotor);
            SendAllMotorsCommand = new DelegateCommand(OnSendAllMotors);

            RefreshPorts();
        }

        // ── CAN connect ────────────────────────────────────────────────────────
        private void OnCanConnect()
        {
            try
            {
                IsConnected = !IsConnected;
                var msg = IsConnected
                    ? $"CAN connected — Port: {SelectedPort}  Baud: {SelectedBaud}  MotorID: {Config.MotorId}"
                    : "CAN disconnected";
                AppendLog(msg);
                LogHelper.Debug(msg);
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        // ── Port scanning ──────────────────────────────────────────────────────
        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames();
            AvailablePorts.Clear();
            foreach (var p in ports)
                AvailablePorts.Add(p);

            if (AvailablePorts.Count > 0)
            {
                SelectedPort = AvailablePorts.Contains(Config.SerialPort)
                    ? Config.SerialPort
                    : AvailablePorts[0];
            }
            else
            {
                SelectedPort = string.Empty;
            }

            AppendLog($"Ports refreshed — found: {(AvailablePorts.Count > 0 ? string.Join(", ", AvailablePorts) : "none")}");
        }

        // ── Test cases ─────────────────────────────────────────────────────────
        private void OnRunTestCase(int number, string label)
        {
            if (!IsConnected)
            {
                AppendLog($"[TC{number} {label}] ERROR — CAN not connected. Connect first.");
                return;
            }

            try
            {
                byte cmdByte   = (byte)(0x10 + number);
                string canId   = Config.MotorId;
                string txFrame = $"ID={canId}  DLC=8  Data=[{cmdByte:X2} {number:X2} 00 00 00 00 00 00]";

                AppendLog($"[TC{number} {label}] SEND  {txFrame}");
                LogHelper.Debug($"TC{number} TX: {txFrame}");

                SimulateCanResponse(number, label, canId, cmdByte);
            }
            catch (Exception ex)
            {
                AppendLog($"[TC{number} {label}] EXCEPTION: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        private void SimulateCanResponse(int number, string label, string canId, byte cmdByte)
        {
            bool success    = true;   // swap to real CAN read result
            byte statusByte = success ? (byte)0x00 : (byte)0xFF;
            string rxFrame  = $"ID={canId}  DLC=8  Data=[{cmdByte:X2} {number:X2} {statusByte:X2} 00 00 00 00 00]";
            string result   = success ? "OK" : "FAIL";

            AppendLog($"[TC{number} {label}] RECV  {rxFrame}");
            AppendLog($"[TC{number} {label}] RESULT → {result}");
            LogHelper.Debug($"TC{number} RX: {rxFrame}  Result={result}");
        }

        // ── Motor control ──────────────────────────────────────────────────────
        private void OnZeroCmd()
        {
            foreach (var m in Motors)
            {
                m.QCmd   = 0;
                m.DqCmd  = 0;
                m.TauCmd = 0;
            }
            AppendLog("All commands zeroed");
        }

        private void OnHoldPosition()
        {
            foreach (var m in Motors)
                m.QCmd = Math.Round(m.Q, 4);
            AppendLog("Hold current position applied");
        }

        private void OnSetAllKp()
        {
            foreach (var m in Motors)
                m.Kp = AllKp;
        }

        private void OnSetAllKd()
        {
            foreach (var m in Motors)
                m.Kd = AllKd;
        }

        // ── Send ───────────────────────────────────────────────────────────────
        private void OnSendMotor(MotorChannelModel? m)
        {
            if (m == null) return;
            if (!IsConnected)
            {
                AppendLog($"[{m.Label}] SEND ERROR — CAN not connected.");
                return;
            }
            var frame = BuildMotorFrame(m);
            AppendLog($"[{m.Label}] SEND  {frame}");
            LogHelper.Debug($"{m.Label} TX: {frame}");
        }

        private void OnSendAllMotors()
        {
            if (!IsConnected)
            {
                AppendLog("[SendAll] ERROR — CAN not connected.");
                return;
            }
            int sent = 0;
            foreach (var m in Motors)
            {
                if (!m.Enabled) continue;
                var frame = BuildMotorFrame(m);
                AppendLog($"[{m.Label}] SEND  {frame}");
                LogHelper.Debug($"{m.Label} TX: {frame}");
                sent++;
            }
            AppendLog($"[SendAll] {sent} motor(s) sent.");
        }

        private string BuildMotorFrame(MotorChannelModel m)
        {
            // Pack floats into 8-byte CAN frame (placeholder encoding)
            int qRaw   = (int)(m.QCmd  * 1000);
            int dqRaw  = (int)(m.DqCmd * 1000);
            int tauRaw = (int)(m.TauCmd * 100);
            return $"ID={Config.MotorId}  DLC=8  Data=[{m.Id:X2} {qRaw & 0xFF:X2} {(qRaw >> 8) & 0xFF:X2} {dqRaw & 0xFF:X2} {tauRaw & 0xFF:X2} {(int)(m.Kp):X2} {(int)(m.Kd * 10):X2} 00]";
        }

        // ── Save log ───────────────────────────────────────────────────────────
        public string SaveLogButtonText => _isLogging ? "Stop Log" : "Save Log";
        private void OnSaveLog()
        {
            if (_isLogging)
            {
                _isLogging = false;
                RaisePropertyChanged(nameof(SaveLogButtonText));

                _logWriter?.Close();
                _logWriter = null;
                LogStatusText = "Not logging";
                AppendLog("Log stopped");
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new SaveFileDialog
                {
                    Title      = "Save Motor Log",
                    Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName   = Config.LogFile,
                    DefaultExt = ".csv"
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    _logWriter = new StreamWriter(dlg.FileName, append: false, Encoding.UTF8);
                    _logWriter.WriteLine("time,motor,q,dq,tau,temp,error");
                    _isLogging = true;
                    RaisePropertyChanged(nameof(SaveLogButtonText));

                    Config.LogFile = dlg.FileName;
                    LogStatusText = $"Logging: {dlg.FileName}";
                    AppendLog($"Log started: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    LogHelper.Exception(ex);
                    AppendLog($"Log error: {ex.Message}");
                }
            });
        }

        // ── Config load / save ─────────────────────────────────────────────────
        private void OnLoadConfig()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new OpenFileDialog
                {
                    Title  = "Load Config File",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var cfg  = JsonSerializer.Deserialize<MotorConfig>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        RaisePropertyChanged(nameof(Config));

                        // Sync port/baud ViewModel properties from the loaded config
                        SelectedBaud = cfg.BaudRate;
                        SelectedPort = AvailablePorts.Contains(cfg.SerialPort)
                            ? cfg.SerialPort
                            : (AvailablePorts.Count > 0 ? AvailablePorts[0] : string.Empty);

                        AppendLog($"Config loaded: {dlg.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Exception(ex);
                    AppendLog($"Config load error: {ex.Message}");
                }
            });
        }

        private void OnSaveConfig()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new SaveFileDialog
                {
                    Title      = "Save Config File",
                    Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName   = "motor_config.json",
                    DefaultExt = ".json"
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(Config, opts);
                    File.WriteAllText(dlg.FileName, json);
                    AppendLog($"Config saved: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    LogHelper.Exception(ex);
                    AppendLog($"Config save error: {ex.Message}");
                }
            });
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        public void AppendLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}]  {message}";
            LogEntries.Insert(0, entry);

            if (_isLogging && _logWriter != null)
            {
                try { _logWriter.WriteLine(entry); }
                catch (Exception ex) { LogHelper.Exception(ex); }
            }
        }

        private void OnGoBack()
        {
            if (_isLogging)
            {
                _isLogging = false;
                _logWriter?.Close();
                _logWriter = null;
            }
            _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
        }
    }
}
