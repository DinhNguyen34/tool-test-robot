using Common.Core.Helpers;
using Microsoft.Win32;
using ModuleMotor.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.RightsManagement;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace ModuleMotor.ViewModels
{
    public class MotorViewModel : BindableBase
    {
        private const int N_MOTORS = 8;
        private readonly IRegionManager _regionManager;

        private string _newTestCaseCode = string.Empty;
        public string NewTestCaseCode
        {
            get => _newTestCaseCode;
            set => SetProperty(ref _newTestCaseCode, value);
        }
        private string _newTestCaseLabel = string.Empty;
        public string NewTestCaseLabel
        {
            get => _newTestCaseLabel;
            set => SetProperty(ref _newTestCaseLabel, value);
        }
        private string _newTestCaseDescription = string.Empty;
        public string NewTestCaseDescription
        {
            get => _newTestCaseDescription;
            set => SetProperty(ref _newTestCaseDescription, value);
        }
        private string _newCanPayload = string.Empty;
        public string NewCanPayload
        {
            get => _newCanPayload;
            set => SetProperty(ref _newCanPayload, value);
        }
        private string _newExpectedRespone = string.Empty;
        public string NewExpectedRespone
        {
            get => _newExpectedRespone;
            set => SetProperty(ref _newExpectedRespone, value);
        }
        private int _newTimeoutMs = 1000;
        public int NewTimeoutMs
        {
            get => _newTimeoutMs;
            set => SetProperty(ref _newTimeoutMs, value);
        }
        private CanSendMode _newDefaultSendMode = CanSendMode.SendOnce;
        public CanSendMode NewDefaultSendMode
        {
            get => _newDefaultSendMode;
            set => SetProperty(ref _newDefaultSendMode, value);
        }
        private int _newIntervalMs = 2;
        public int NewIntervalMs
        {
            get => _newIntervalMs;
            set => SetProperty(ref _newIntervalMs, value);
        }

        private string _rawCanId = "0x141";
        public string RawCanId
        {
            get => _rawCanId;
            set => SetProperty(ref _rawCanId, value);
        }

        private string _rawCanData = "01 02 03 04 05 06 07 08";
        public string RawCanData
        {
            get => _rawCanData;
            set => SetProperty(ref _rawCanData, value);
        }

        private string _rawSendStatus = "Ready to send raw CAN frame.";
        public string RawSendStatus
        {
            get => _rawSendStatus;
            set => SetProperty(ref _rawSendStatus, value);
        }


        private TestCaseDefinition? _selectedQuickLibraryTestCase;
        public TestCaseDefinition? SelectedQuickLibraryTestCase
        {
            get => _selectedQuickLibraryTestCase;
            set => SetProperty(ref _selectedQuickLibraryTestCase, value);
        }


        public IReadOnlyList<CanSendMode> CanSendModes { get; } = Enum.GetValues<CanSendMode>();

        public MotorModel Model { get; } = new MotorModel();
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
                if (value) StartRxLoop();
                else       StopRxLoop();
            }
        }
        public string ConnectLabel  => IsConnected ? "DISCONNECT CAN"        : "CAN Connect";
        public string CanStatusText => IsConnected ? "CONNECTED"   : "DISCONNECTED";

        // ── Live RX ────────────────────────────────────────────────────────────
        public ObservableCollection<RxFrameEntry> RxFrames { get; } = new();
        private CancellationTokenSource? _rxCts;
        private int _lastRxCount;

        private string _rxStatusText = "Not receiving";
        public string RxStatusText
        {
            get => _rxStatusText;
            set => SetProperty(ref _rxStatusText, value);
        }

        public DelegateCommand ClearRxCommand { get; private set; } = null!;

        // ── Port selection ─────────────────────────────────────────────────────
        public ObservableCollection<string> AvailablePorts { get; } = new();

        public static IReadOnlyList<int> BaudRates { get; } = new[]
            {19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000};

        public static IReadOnlyList<int> CanBitratesKbps { get; } = new[]
            {50, 100, 120, 200, 250, 400, 500, 800, 1000, 1200, 1500, 2000};

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

        private int _selectedCanBitrate = 1000;
        public int SelectedCanBitrate
        {
            get => _selectedCanBitrate;
            set
            {
                SetProperty(ref _selectedCanBitrate, value);
                Config.CanBitrateKbps = value;
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
        public ObservableCollection<TestCaseDefinition> QuickLibraryTestCases { get; } = new();
        public ObservableCollection<TestCaseDefinition> AvailableTestCases { get; } = new();
        public ObservableCollection<BuiltTestStep> BuiltSteps { get; } = new();

        private TestCaseDefinition? _selectedAvailableTestCase;
        public TestCaseDefinition? SelectedAvailableTestCase
        {
            get => _selectedAvailableTestCase;
            set => SetProperty(ref _selectedAvailableTestCase, value);
        }

        private BuiltTestStep? _selectedBuiltStep;
        public BuiltTestStep? SelectedBuiltStep
        {
            get => _selectedBuiltStep;
            set => SetProperty(ref _selectedBuiltStep, value);
        }

        private string _testPlanName = "New Test Plan";
        public string TestPlanName
        {
            get => _testPlanName;
            set => SetProperty(ref _testPlanName, value);
        }

        private bool _isAddTestCasePopupOpen;
        public bool IsAddTestCasePopupOpen
        {
            get => _isAddTestCasePopupOpen;
            set => SetProperty(ref _isAddTestCasePopupOpen, value);
        }

        private bool _isAddConfigPopupOpen;
        public bool IsAddConfigPopupOpen
        {
            get => _isAddConfigPopupOpen;
            set => SetProperty(ref _isAddConfigPopupOpen, value);
        }

        // ── Commands ───────────────────────────────────────────────────────────
        //public DelegateCommand CanConnectCommand { get; }
        public DelegateCommand RunBuiltStepsCommand { get; }
        public DelegateCommand ZeroCmdCommand { get; }
        public DelegateCommand HoldPositionCommand { get; }
        public DelegateCommand SetAllKpCommand { get; }
        public DelegateCommand SetAllKdCommand { get; }
        public DelegateCommand SaveLogCommand { get; }
        public DelegateCommand LoadConfigCommand { get; }
        public DelegateCommand SaveConfigCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand GoBackCommand { get; }
        public DelegateCommand RefreshPortsCommand { get; }
        public DelegateCommand<MotorChannelModel> SendMotorCommand { get; }
        public DelegateCommand SendAllMotorsCommand { get; }
        public DelegateCommand OpenAddTestCaseCommand { get; }
        public DelegateCommand AddSelectedTestCaseCommand { get; }
        public DelegateCommand CloseAddTestCaseCommand { get; }
        public DelegateCommand OpenAddConfigCommand { get; }
        public DelegateCommand CloseAddConfigCommand { get; }
        public DelegateCommand RemoveStepCommand { get; }
        public DelegateCommand MoveStepUpCommand { get; }
        public DelegateCommand MoveStepDownCommand { get; }
        public DelegateCommand DuplicatesStepCommand { get; }
        public DelegateCommand ClearBuiltStepCommand { get; }

        public DelegateCommand SaveNewConfigCommand {  get; }
        public DelegateCommand SendRawFrameCommand { get; }

        public DelegateCommand OpenListCan => new DelegateCommand(_openListCan);
        public DelegateCommand CanConnectCommand => new DelegateCommand(OnCanConnect);

        public DelegateCommand AddQuickLibraryStepCommand { get; }
        // ── Constructor ────────────────────────────────────────────────────────
        public MotorViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;

            for (int i = 0; i < N_MOTORS; i++)
            {
                //Motors.Add(new MotorChannelModel(i + 1));
                var motor = new MotorChannelModel(i + 1)
                {
                    DeviceId = (byte)(i + 1)
                };
                Motors.Add(motor);
            }
            

            var tcDefs = new (string Label, RsCommandType Cmd)[]
            {
                ("Get Device ID",        RsCommandType.GetId),
                ("Motor Enabled",        RsCommandType.Enable),
                ("Motor Stop",           RsCommandType.Disable),
                ("Set Zero Mechanical",  RsCommandType.SetZero),
                ("Max Position",         RsCommandType.Control),
                ("Min Position",         RsCommandType.Control),
                ("Get State Motor",      RsCommandType.None),
                ("Max Speed",            RsCommandType.Control),
            };
            for (int i = 0; i < tcDefs.Length; i++)
            {
                var tc = new TestCaseDefinition
                {
                    Number = i + 1,
                    Code = $"TC{i + 1:000}",
                    Label = tcDefs[i].Label,
                    Description = $"Built-in testcase: {tcDefs[i].Label}",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = tcDefs[i].Cmd,
                };
                AvailableTestCases.Add(tc);
                if (i < 4)
                    QuickLibraryTestCases.Add(tc);
            }

            for (int i = 0; i < N_MOTORS; i++)
            {
                var num = i + 1;
                var label = tcDefs[i].Label;
                TestCases.Add(new TestCaseItem
                {
                    Number = num,
                    Label = label,
                    Command = new DelegateCommand(() => OnRunTestCase(num, label))
                });
            }

            OpenAddTestCaseCommand = new DelegateCommand(() =>
            {
                IsAddConfigPopupOpen = false;
                IsAddTestCasePopupOpen = true;
            });
            CloseAddTestCaseCommand = new DelegateCommand(() => IsAddTestCasePopupOpen = false);
            AddSelectedTestCaseCommand = new DelegateCommand(AddSelectedTestCase);

            OpenAddConfigCommand = new DelegateCommand(() =>
            {
                ResetNewConfigForm();
                IsAddTestCasePopupOpen = false;
                IsAddConfigPopupOpen = true;
            });
            CloseAddConfigCommand = new DelegateCommand(() => IsAddConfigPopupOpen = false);

            RemoveStepCommand = new DelegateCommand(RemoveSelectedStep);
            MoveStepUpCommand = new DelegateCommand(MoveSelectedStepUp);
            MoveStepDownCommand = new DelegateCommand(MoveSelectedStepDown);
            DuplicatesStepCommand = new DelegateCommand(DuplicateSelectedStep);
            ClearBuiltStepCommand = new DelegateCommand(ClearBuiltSteps);


            AddQuickLibraryStepCommand = new DelegateCommand(AddQuickLibraryStep);

            RunBuiltStepsCommand = new DelegateCommand(OnRunBuiltSteps);

            ZeroCmdCommand = new DelegateCommand(OnZeroCmd);
            HoldPositionCommand = new DelegateCommand(OnHoldPosition);
            SetAllKpCommand = new DelegateCommand(OnSetAllKp);
            SetAllKdCommand = new DelegateCommand(OnSetAllKd);
            SaveLogCommand = new DelegateCommand(OnSaveLog);
            LoadConfigCommand = new DelegateCommand(OnLoadConfig);
            SaveConfigCommand = new DelegateCommand(OnSaveConfig);
            ClearLogCommand = new DelegateCommand(() => LogEntries.Clear());
            GoBackCommand = new DelegateCommand(OnGoBack);
            //RefreshPortsCommand = new DelegateCommand(RefreshPorts);
            SendMotorCommand = new DelegateCommand<MotorChannelModel>(OnSendMotor);
            SendAllMotorsCommand = new DelegateCommand(OnSendAllMotors);
            SaveNewConfigCommand = new DelegateCommand(SaveNewConfig);
            SendRawFrameCommand = new DelegateCommand(OnSendRawFrame);
            ClearRxCommand      = new DelegateCommand(() => { RxFrames.Clear(); _lastRxCount = 0; });

            //RefreshPorts();
            SelectedCanBitrate = Config.CanBitrateKbps > 0 ? Config.CanBitrateKbps : 1000;
        }

        // ── CAN connect ────────────────────────────────────────────────────────
        private void OnCanConnect()
        {
            try
            {
                if (HandleCanConnect())
                    return;

                IsConnected = !IsConnected;

                var msg = IsConnected
                    ? $"CAN connected — Port: {SelectedPort}  Baud: {SelectedBaud}  MotorID: {Config.MotorId}"
                    : "CAN disconnected";
                AppendLog(msg);
                LogHelper.Debug(msg);
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }


        // ---- Send testcase sequence in Builder---------------------------------
        private async void OnRunBuiltSteps()
        {
            if (!IsConnected)
            {
                AppendLog("[Builder] Error - Can not Connect.");
                return;
            }
            if (BuiltSteps.Count == 0)
            {
                AppendLog("[Builder] No step to run");
                return;
            }
            foreach (var step in BuiltSteps.OrderBy(x => x.StepNo))
            {
                if (!step.IsEnabled)
                {
                    step.State = StepRunState.Skipped;
                    step.LastResult = "Skipped";
                    continue;
                }
                step.State = StepRunState.Running;
                step.LastResult = "Running";

                if (step.DelayBeforeMs > 0)
                    await Task.Delay(step.DelayBeforeMs);

                var ok = true;
                var repeat = Math.Max(1, step.RepeatCount);
                var stepStart = Environment.TickCount64;

                for (int i = 0; i < repeat; i++)
                {
                    var elapsed = (int)(Environment.TickCount64 - stepStart);
                    var remaining = step.TimeoutMs - elapsed;
                    if (remaining <= 0)
                    {
                        step.LastResult = "Timeout";
                        ok = false;
                        AppendLog("Step Timeout");
                        break;
                    }
                    try
                    {
                        TryRunRealTestCase(step.TestCaseNumber, step.Label);
                        if (step.SendMode == CanSendMode.Continuous && i < repeat - 1)
                            await Task.Delay(Math.Max(1, step.IntervalMs));
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        AppendLog($"[Builder] {step.Label} Error - {ex.Message}");
                        break;
                    }
                }

                step.State = ok ? StepRunState.Passed : StepRunState.Failed;
                step.LastResult = ok ? "Done" : "Failed";
            }

            AppendLog("[Builder] Send All complected.");

        }
        // ── Port scanning ──────────────────────────────────────────────────────
        //private void RefreshPorts()
        //{
        //    var ports = SerialPort.GetPortNames();
        //    AvailablePorts.Clear();
        //    foreach (var p in ports)
        //        AvailablePorts.Add(p);

        //    if (AvailablePorts.Count > 0)
        //    {
        //        SelectedPort = AvailablePorts.Contains(Config.SerialPort)
        //            ? Config.SerialPort
        //            : AvailablePorts[0];
        //    }
        //    else
        //    {
        //        SelectedPort = string.Empty;
        //    }

        //    AppendLog($"Ports refreshed — found: {(AvailablePorts.Count > 0 ? string.Join(", ", AvailablePorts) : "none")}");
        //}

        private void _openListCan()
        {
            try
            {
                Model.GetListCans();
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
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
                if (TryRunRealTestCase(number, label))
                    return;

                AppendLog($"[TC{number} {label}] No RS command mapped for this test case.");
            }
            catch (Exception ex)
            {
                AppendLog($"[TC{number} {label}] EXCEPTION: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        private void SimulateCanResponse(int number, string label, string canId, byte cmdByte)
        {
            bool success = true;   // swap to real CAN read result
            byte statusByte = success ? (byte)0x00 : (byte)0xFF;
            string rxFrame = $"ID={canId}  DLC=8  Data=[{cmdByte:X2} {number:X2} {statusByte:X2} 00 00 00 00 00]";
            string result = success ? "OK" : "FAIL";

            AppendLog($"[TC{number} {label}] RECV  {rxFrame}");
            AppendLog($"[TC{number} {label}] RESULT → {result}");
            LogHelper.Debug($"TC{number} RX: {rxFrame}  Result={result}");
        }

        // ── Motor control ──────────────────────────────────────────────────────
        private void OnZeroCmd()
        {
            foreach (var m in Motors)
            {
                m.QCmd = 0;
                m.DqCmd = 0;
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
            if (TrySendMotorFrame(m))
                return;

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
            if (TrySendAllMotors())
                return;

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
            return FormatCanFrame(Config.MotorId, BuildMotorPayload(m));
        }

        private bool HandleCanConnect()
        {
            if (IsConnected || Model.GetOpenStatus())
            {
                Model.Close();
                IsConnected = false;
                const string disconnectMessage = "CAN disconnected";
                AppendLog(disconnectMessage);
                LogHelper.Debug(disconnectMessage);
                return true;
            }

            if (Model.SelectedCan == null)
            {
                Model.GetListCans();
                if (Model.SelectedCan == null)
                {
                    AppendLog("CAN connect error - no CAN device selected.");
                    return true;
                }
            }

            var rawLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "can_raw.log");
            var connected = Model.Connect(SelectedCanBitrate, rawLogPath, out var statusMessage);
            IsConnected = connected;

            var message = connected
                ? $"CAN connected - Device: {Model.SelectedCan?.DisplayName ?? "unknown"}  CAN: {SelectedCanBitrate} kbps  MotorID: {Config.MotorId}. {statusMessage}"
                : $"CAN connect failed - {statusMessage}";

            AppendLog(message);
            LogHelper.Debug(message);
            return true;
        }

        private bool TryRunRealTestCase(int number, string label)
        {
            var def = AvailableTestCases.FirstOrDefault(tc => tc.Number == number);
            if (def == null) return false;

            var (canId, payload) = BuildRsFrame(def);
            if (canId == null) return false;

            var txFrame = FormatCanFrame(canId, payload!);
            var rxCountBefore = Model.GetCanMessages().Count;

            if (!Model.SendMessage(canId, payload!, out var sendMessage))
            {
                AppendLog($"[TC{number} {label}] SEND ERROR - {sendMessage}");
                return true;
            }

            AppendLog($"[TC{number} {label}] SEND  {txFrame}");
            LogHelper.Debug($"TC{number} TX: {txFrame}");

            var success = AppendReceivedCanFrames($"TC{number} {label}", rxCountBefore);
            AppendLog($"[TC{number} {label}] RESULT -> {(success.HasValue ? (success.Value ? "OK" : "FAIL") : "NO RESPONSE")}");
            return true;
        }

        private (string? CanId, byte[]? Payload) BuildRsFrame(TestCaseDefinition def)
        {
            return def.RsCommand switch
            {
                RsCommandType.GetId   => RsMotorControl.GetIdMotor(DeviceId),
                RsCommandType.Enable  => RsMotorControl.EnableMotor(DeviceId),
                RsCommandType.Disable => RsMotorControl.DisableMotor(DeviceId),
                RsCommandType.SetZero => RsMotorControl.SetZeroPosition(DeviceId),
                RsCommandType.Control => RsMotorControl.ControlMotor(
                    DeviceId, RsMotorControl.Motor6,   // default profile — wire to Config later
                    torque: 06, position: 0, speed: 0,
                    kp: (float)Config.DefaultKp, kd: (float)Config.DefaultKd),
                _ => (null, null)
            };
        }

        private bool TrySendMotorFrame(MotorChannelModel m)
        {
            var (canId, payload) = RsMotorControl.ControlMotor(
                m.DeviceId, m.Profile,
                torque:   (float)m.TauCmd,
                position: (float)m.QCmd,
                speed:    (float)m.DqCmd,
                kp:       (float)m.Kp,
                kd:       (float)m.Kd);
            var frame = FormatCanFrame(canId, payload);
            var rxCountBefore = Model.GetCanMessages().Count;

            if (!Model.SendMessage(canId, payload, out var sendMessage))
            {
                AppendLog($"[{m.Label}] SEND ERROR - {sendMessage}");
                return true;
            }

            AppendLog($"[{m.Label}] SEND  {frame}");
            LogHelper.Debug($"{m.Label} TX: {frame}");
            AppendReceivedCanFrames(m.Label, rxCountBefore, m);
            return true;
        }

        private bool TrySendAllMotors()
        {
            int sent = 0;
            var rxCountBefore = Model.GetCanMessages().Count;

            foreach (var motor in Motors)
            {
                if (!motor.Enabled) continue;

                var (canId, payload) = RsMotorControl.ControlMotor(
                    motor.DeviceId, motor.Profile,
                    torque:   (float)motor.TauCmd,
                    position: (float)motor.QCmd,
                    speed:    (float)motor.DqCmd,
                    kp:       (float)motor.Kp,
                    kd:       (float)motor.Kd);
                var frame = FormatCanFrame(canId, payload);

                if (!Model.SendMessage(canId, payload, out var sendMessage))
                {
                    AppendLog($"[{motor.Label}] SEND ERROR - {sendMessage}");
                    continue;
                }

                AppendLog($"[{motor.Label}] SEND  {frame}");
                LogHelper.Debug($"{motor.Label} TX: {frame}");
                sent++;
            }

            AppendLog($"[SendAll] {sent} motor(s) sent.");
            AppendReceivedCanFrames("SendAll", rxCountBefore);
            return true;
        }

        private static byte[] BuildMotorPayload(MotorChannelModel m)
        {
            int qRaw = (int)(m.QCmd * 1000);
            int dqRaw = (int)(m.DqCmd * 1000);
            int tauRaw = (int)(m.TauCmd * 100);

            return new[]
            {
                (byte)m.Id,
                (byte)(qRaw & 0xFF),
                (byte)((qRaw >> 8) & 0xFF),
                (byte)(dqRaw & 0xFF),
                (byte)(tauRaw & 0xFF),
                (byte)((int)m.Kp & 0xFF),
                (byte)(((int)(m.Kd * 10)) & 0xFF),
                (byte)0x00
            };
        }

        private byte DeviceId => RsMotorControl.ParseDeviceId(Config.MotorId);

        private (string CanId, byte[] Payload) EnableMotor()   => RsMotorControl.EnableMotor(DeviceId);
        private (string CanId, byte[] Payload) DisableMotor()  => RsMotorControl.DisableMotor(DeviceId);
        private (string CanId, byte[] Payload) SetZeroPos()    => RsMotorControl.SetZeroPosition(DeviceId);

        private static string FormatCanFrame(string canId, byte[] payload)
        {
            return $"ID={canId}  DLC={payload.Length}  Data=[{string.Join(" ", payload.Select(b => b.ToString("X2")))}]";
        }

        private bool? AppendReceivedCanFrames(string context, int previousCount,
                                               MotorChannelModel? motor = null)
        {
            Thread.Sleep(20);
            var messages = Model.GetCanMessages();
            if (messages.Count <= previousCount)
            {
                AppendLog($"[{context}] RECV  no response captured.");
                return null;
            }

            bool? latestSuccess = null;
            foreach (var frame in messages.Skip(previousCount))
            {
                var payload = frame.data ?? Array.Empty<byte>();
                latestSuccess = frame.status == 0;

                if (motor != null && payload.Length >= 8)
                {
                    var fb = RsMotorControl.DecodeFeedback(motor.DeviceId, payload, motor.Profile);
                    motor.Q           = Math.Round(fb.Angle,       4);
                    motor.Dq          = Math.Round(fb.Speed,       4);
                    motor.Tau         = Math.Round(fb.Torque,      4);
                    motor.Temperature = Math.Round(fb.Temperature, 2);

                    AppendLog($"[{context}] RECV  Motor {motor.Label} | " +
                              $"Angle={fb.Angle:F4} rad  Speed={fb.Speed:F4} rad/s  " +
                              $"Torque={fb.Torque:F4} Nm  Temp={fb.Temperature:F1}°C  " +
                              $"Status={frame.status:X2}");
                }
                else
                {
                    AppendLog($"[{context}] RECV  Status={frame.status:X2}  " +
                              $"Data=[{string.Join(" ", payload.Select(b => b.ToString("X2")))}]");
                }
            }

            return latestSuccess;
        }

        private void OnSendRawFrame()
        {
            if (!IsConnected)
            {
                RawSendStatus = "CAN is not connected.";
                AppendLog("[RawFrame] SEND ERROR - CAN is not connected.");
                return;
            }

            var canId = RawCanId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(canId))
            {
                RawSendStatus = "CAN ID is empty.";
                AppendLog("[RawFrame] SEND ERROR - CAN ID is empty.");
                return;
            }

            if (!TryParseRawPayload(RawCanData, out var payload, out var parseError))
            {
                RawSendStatus = parseError;
                AppendLog($"[RawFrame] PARSE ERROR - {parseError}");
                return;
            }

            var rxCountBefore = Model.GetCanMessages().Count;
            if (!Model.SendMessage(canId, payload, out var sendMessage))
            {
                RawSendStatus = sendMessage;
                AppendLog($"[RawFrame] SEND ERROR - {sendMessage}");
                return;
            }

            var frame = FormatCanFrame(canId, payload);
            AppendLog($"[RawFrame] SEND  {frame}");
            LogHelper.Debug($"Raw TX: {frame}");

            var success = AppendReceivedCanFrames("RawFrame", rxCountBefore);
            RawSendStatus = success.HasValue
                ? (success.Value ? "Frame sent. Response status OK." : "Frame sent. Response status indicates fail.")
                : "Frame sent. No response captured.";
        }

        private static bool TryParseRawPayload(string? input, out byte[] payload, out string error)
        {
            payload = Array.Empty<byte>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Raw data is empty.";
                return false;
            }

            var normalized = input
                .Replace(",", " ")
                .Replace(";", " ")
                .Replace("-", " ");

            var tokens = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = "No byte found in raw data.";
                return false;
            }

            if (tokens.Length > 64)
            {
                error = "Raw CAN payload supports up to 64 bytes.";
                return false;
            }

            var bytes = new List<byte>(tokens.Length);
            foreach (var token in tokens)
            {
                var value = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? token[2..]
                    : token;

                if (!byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    error = $"Invalid hex byte: {token}";
                    return false;
                }

                bytes.Add(parsed);
            }

            payload = bytes.ToArray();
            return true;
        }

        // --- New Config (save and reset)----------------------------------------
        private void ResetNewConfigForm()
        {
            NewTestCaseCode = string.Empty;
            NewTestCaseLabel = string.Empty;
            NewTestCaseDescription = string.Empty;
            NewCanPayload = string.Empty;
            NewExpectedRespone = string.Empty;
            NewTimeoutMs = 1000;
            NewDefaultSendMode = CanSendMode.SendOnce;
            NewIntervalMs = 2;
        }

        private void SaveNewConfig()
        {
            if (string.IsNullOrWhiteSpace(NewTestCaseCode))
                return;
            var nextNumber = AvailableTestCases.Count == 0
                ? 1
                : AvailableTestCases.Max(x => x.Number) + 1;
            var testCase = new TestCaseDefinition
            {
                Number = nextNumber,
                Code = string.IsNullOrWhiteSpace(NewTestCaseCode) ? $"TC{nextNumber:000}" : NewTestCaseCode,
                Label = NewTestCaseLabel.Trim(),
                Description = NewTestCaseDescription.Trim(),
                CanPayLoad = NewCanPayload.Trim(),
                ExpectedRespone = NewExpectedRespone.Trim(),
                TimeoutMs = NewTimeoutMs,
                DefaultSendMode = NewDefaultSendMode,
                IntervalMs = NewIntervalMs,
                IsbuiltIn = false
            };
            AvailableTestCases.Add(testCase);
            SelectedAvailableTestCase = testCase;

            AppendLog($"New Testcase Config Added: {testCase.Label}");
            
            IsAddConfigPopupOpen = false;
            ResetNewConfigForm();
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
                    Title = "Save Motor Log",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = Config.LogFile,
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
                    Title = "Load Config File",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var cfg = JsonSerializer.Deserialize<MotorConfig>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        RaisePropertyChanged(nameof(Config));

                        // Sync port/baud ViewModel properties from the loaded config
                        SelectedBaud = cfg.BaudRate;
                        SelectedCanBitrate = cfg.CanBitrateKbps > 0 ? cfg.CanBitrateKbps : 1000;
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
                    Title = "Save Config File",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "motor_config.json",
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
        private void ReindexBuiltSteps()
        {
            for (int i = 0; i < BuiltSteps.Count; i++)
                BuiltSteps[i].StepNo = i + 1;
        }
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

        // ── RX loop ────────────────────────────────────────────────────────────
        private void StartRxLoop()
        {
            StopRxLoop();
            _lastRxCount = 0;
            _rxCts = new CancellationTokenSource();
            Task.Run(() => RxLoopAsync(_rxCts.Token));
            RxStatusText = "Receiving…";
        }

        private void StopRxLoop()
        {
            _rxCts?.Cancel();
            _rxCts = null;
            RxStatusText = "Not receiving";
        }

        private async Task RxLoopAsync(CancellationToken ct)
        {
            var intervalMs = Config.RefreshHz > 0 ? Math.Max(2, 40 / Config.RefreshHz) : 50;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var messages = Model.GetCanMessages();
                    if (messages.Count > _lastRxCount)
                    {
                        var newFrames = messages.Skip(_lastRxCount).ToList();
                        _lastRxCount  = messages.Count;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var raw in newFrames)
                            {
                                var payload = raw.data ?? Array.Empty<byte>();
                                var entry = new RxFrameEntry
                                {
                                    Timestamp = DateTime.Now,
                                    Channel   = raw.ch,
                                    Status    = raw.status,
                                    Data      = payload
                                };

                                RxFrames.Insert(0, entry);
                                if (RxFrames.Count > 1000)
                                    RxFrames.RemoveAt(RxFrames.Count - 1);

                                if (payload.Length >= 8)
                                {
                                    // Motor ID is from context — match by channel (ch = DeviceId)
                                    var motor = Motors.FirstOrDefault(m => m.DeviceId == raw.ch);
                                    var profile = motor?.Profile ?? RsMotorControl.Motor4;
                                    var fb = RsMotorControl.DecodeFeedback((byte)raw.ch, payload, profile);

                                    if (motor != null)
                                    {
                                        motor.Q           = Math.Round(fb.Angle,       4);
                                        motor.Dq          = Math.Round(fb.Speed,       4);
                                        motor.Tau         = Math.Round(fb.Torque,      4);
                                        motor.Temperature = Math.Round(fb.Temperature, 2);
                                    }

                                    AppendLog($"[RX] Motor {fb.MotorId:D2} | CH={raw.ch} | " +
                                              $"Angle={fb.Angle:F4} rad  Speed={fb.Speed:F4} rad/s  " +
                                              $"Torque={fb.Torque:F4} Nm  Temp={fb.Temperature:F1}°C  " +
                                              $"Status={raw.status:X2}");
                                }
                                else
                                {
                                    AppendLog($"[RX] CH={raw.ch} {entry.StatusText}  {entry.DataHex}");
                                }
                            }

                            RxStatusText = $"Receiving — {_lastRxCount} frame(s) total";
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Exception(ex);
                }

                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }
        }

        private void OnGoBack()
        {
            StopRxLoop();
            if (_isLogging)
            {
                _isLogging = false;
                _logWriter?.Close();
                _logWriter = null;
            }
            _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
        }
        // --AddStep------------------------------------------------------------------
        public void AddSelectedTestCase()
        {

            if (SelectedAvailableTestCase == null) return;

            AddBuiltStepFromDefinition(SelectedAvailableTestCase);
            IsAddTestCasePopupOpen = false;
        }
        private void RemoveSelectedStep()
        {
            if (SelectedBuiltStep == null) return;
            BuiltSteps.Remove(SelectedBuiltStep);
            ReindexBuiltSteps();
        }
        private void MoveSelectedStepUp()
        {
            if (SelectedBuiltStep == null) return;
            var index = BuiltSteps.IndexOf(SelectedBuiltStep);
            if (index <= 0) return;

            BuiltSteps.Move(index, index - 1);
            ReindexBuiltSteps();
        }
        private void MoveSelectedStepDown()
        {
            if (SelectedBuiltStep == null) return;
            var index = BuiltSteps.IndexOf(SelectedBuiltStep);
            if (index <0 || index >= BuiltSteps.Count - 1) return;

            BuiltSteps.Move(index, index + 1);
            ReindexBuiltSteps();
        }
        private void AddBuiltStepFromDefinition(TestCaseDefinition testCase)
        {
            BuiltSteps.Add(new BuiltTestStep
            {
                StepNo = BuiltSteps.Count + 1,
                TestCaseNumber = testCase.Number,
                Label = testCase.Label,
                SendMode = testCase.DefaultSendMode,
                IntervalMs = testCase.IntervalMs,
                TimeoutMs = testCase.TimeoutMs,
                RepeatCount = 1,
                DelayBeforeMs = 0,
                IsEnabled = true
            }
                );
            ReindexBuiltSteps();
        }
        private void DuplicateSelectedStep()
        {
            if (SelectedBuiltStep == null) return;
            var copy = new BuiltTestStep
            {
                TestCaseNumber = SelectedBuiltStep.TestCaseNumber,
                Label = SelectedBuiltStep.Label,
                IsEnabled = SelectedBuiltStep.IsEnabled,
                SendMode = SelectedBuiltStep.SendMode,
                IntervalMs = SelectedBuiltStep.IntervalMs,
                RepeatCount = SelectedBuiltStep.RepeatCount,
                DelayBeforeMs = SelectedBuiltStep.DelayBeforeMs,
                TimeoutMs = SelectedBuiltStep.TimeoutMs,
                State = StepRunState.Pending,
                LastResult = string.Empty
            };
            var index = BuiltSteps.IndexOf(SelectedBuiltStep);
            BuiltSteps.Insert(index + 1, copy);
            ReindexBuiltSteps();
        }
        private void ClearBuiltSteps()
        {

            foreach (var step in BuiltSteps.Where(s => s.IsEnabled).ToList())
                BuiltSteps.Remove(step);
            ReindexBuiltSteps();
        }

        private void AddQuickLibraryStep()
        {
            if (SelectedQuickLibraryTestCase == null)
                return;
            AddBuiltStepFromDefinition(SelectedQuickLibraryTestCase);
        }

    }
}
