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
using System.Threading.Channels;
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

        private float _newTargetPosition;
        public float NewTargetPosition
        {
            get => _newTargetPosition;
            set => SetProperty(ref _newTargetPosition, value);
        }

        private float _newTargetSpeed;
        public float NewTargetSpeed
        {
            get => _newTargetSpeed;
            set => SetProperty(ref _newTargetSpeed, value);
        }

        private float _newTargetTorque;
        public float NewTargetTorque
        {
            get => _newTargetTorque;
            set => SetProperty(ref _newTargetTorque, value);
        }

        private float _newTargetKp = 20f;
        public float NewTargetKp
        {
            get => _newTargetKp;
            set => SetProperty(ref _newTargetKp, value);
        }

        private float _newTargetKd = 1f;
        public float NewTargetKd
        {
            get => _newTargetKd;
            set => SetProperty(ref _newTargetKd, value);
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
        public ObservableCollection<TestCaseItem> TestCases { get; } = new();

        // ── Config ─────────────────────────────────────────────────────────────
        private MotorConfig _config = new();
        public MotorConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public IReadOnlyList<MotorProtocolKind> SupportedProtocols { get; } = Enum.GetValues<MotorProtocolKind>();

        private MotorProtocolKind _selectedProtocol = MotorProtocolKind.Robstride;
        public MotorProtocolKind SelectedProtocol
        {
            get => _selectedProtocol;
            set
            {
                if (!SetProperty(ref _selectedProtocol, value))
                    return;

                Config.Protocol = value;
                ResetRawFrameDefaults();
                RebuildBuiltInTestCases();
                BuiltSteps.Clear();
                SelectedAvailableTestCase = null;
                SelectedQuickLibraryTestCase = null;
                RaisePropertyChanged(nameof(CurrentProtocolUsesExtendedId));
            }
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
        private int    _lastRxCount;
        private double _lastRxTime = -1.0;

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

        public bool CurrentProtocolUsesExtendedId => SelectedProtocol == MotorProtocolKind.Robstride;

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
        public ObservableCollection<string> LogEntries       { get; } = new();
        public ObservableCollection<string> TestResultEntries { get; } = new();
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
        public DelegateCommand? RefreshPortsCommand { get; }
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
            ClearRxCommand      = new DelegateCommand(() => { RxFrames.Clear(); _lastRxCount = 0; _lastRxTime = -1.0; });

            //RefreshPorts();
            SelectedCanBitrate = Config.CanBitrateKbps > 0 ? Config.CanBitrateKbps : 1000;
            SelectedProtocol = Config.Protocol;
            RebuildBuiltInTestCases();
            ResetRawFrameDefaults();
        }

        private void ResetRawFrameDefaults()
        {
            if (SelectedProtocol == MotorProtocolKind.Encos)
            {
                RawCanId = "0x7FF";
                RawCanData = "00 01 00 03";
                RawSendStatus = "Ready to send ENCOS standard CAN frame.";
                return;
            }

            RawCanId = "0x141";
            RawCanData = "01 02 03 04 05 06 07 08";
            RawSendStatus = "Ready to send Robstride extended CAN frame.";
        }

        private void RebuildBuiltInTestCases()
        {
            AvailableTestCases.Clear();
            QuickLibraryTestCases.Clear();
            TestCases.Clear();

            var definitions = SelectedProtocol == MotorProtocolKind.Encos
                ? CreateEncosBuiltInDefinitions()
                : CreateRobstrideBuiltInDefinitions();

            foreach (var definition in definitions)
            {
                AvailableTestCases.Add(definition);
                if (QuickLibraryTestCases.Count < 4)
                    QuickLibraryTestCases.Add(definition);

                int number = definition.Number;
                TestCases.Add(new TestCaseItem
                {
                    Number = number,
                    Label = definition.Label,
                    Command = new DelegateCommand(() => OnRunTestCase(number, definition.Label))
                });
            }

            while (TestCases.Count < N_MOTORS)
            {
                int number = TestCases.Count + 1;
                TestCases.Add(new TestCaseItem
                {
                    Number = number,
                    Label = $"Unused {number}",
                    Command = new DelegateCommand(() => AppendLog($"[TC{number}] No built-in testcase mapped for {SelectedProtocol}."))
                });
            }

            RaisePropertyChanged(nameof(TestCases));
        }

        private List<TestCaseDefinition> CreateRobstrideBuiltInDefinitions()
        {
            var tcDefs = new (string Label, RsCommandType Cmd)[]
            {
                ("Get Device ID",        RsCommandType.GetId),
                ("Motor Enabled",        RsCommandType.Enable),
                ("Motor Stop",           RsCommandType.Disable),
                ("Set Zero Mechanical",  RsCommandType.SetZero),
                ("Max Position",         RsCommandType.Control),
                ("Min Position",         RsCommandType.Control),
                ("Get State Motor",      RsCommandType.Enable),
                ("Max Speed",            RsCommandType.Control),
            };

            var result = new List<TestCaseDefinition>(tcDefs.Length);
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
                    TargetTorque = 0f,
                    TargetPosition = 0f,
                    TargetSpeed = 0f,
                    TargetKp = (float)Config.DefaultKp,
                    TargetKd = (float)Config.DefaultKd,
                };

                switch (tc.Label)
                {
                    case "Max Position":
                        tc.TargetPosition = 12.57f;
                        break;
                    case "Min Position":
                        tc.TargetPosition = -12.57f;
                        break;
                    case "Max Speed":
                        tc.UseResolvedProfileMaxSpeed = true;
                        tc.TargetKp = 0;
                        break;
                }

                result.Add(tc);
            }

            return result;
        }

        private List<TestCaseDefinition> CreateEncosBuiltInDefinitions()
        {
            return new List<TestCaseDefinition>
            {
                new()
                {
                    Number = 1,
                    Code = "TC001",
                    Label = "Query CAN ID",
                    Description = "ENCOS built-in testcase: query current CAN ID.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.QueryCanId
                },
                new()
                {
                    Number = 2,
                    Code = "TC002",
                    Label = "Set Zero Position",
                    Description = "ENCOS built-in testcase: set current position as zero.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.SetZero
                },
                new()
                {
                    Number = 3,
                    Code = "TC003",
                    Label = "Position 0 deg",
                    Description = "ENCOS servo position control to 0 degrees.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.PositionControl,
                    TargetPosition = 0f,
                    TargetSpeed = 50f,
                    TargetTorque = 10f
                },
                new()
                {
                    Number = 4,
                    Code = "TC004",
                    Label = "Position 90 deg",
                    Description = "ENCOS servo position control to 90 degrees.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.PositionControl,
                    TargetPosition = 90f,
                    TargetSpeed = 50f,
                    TargetTorque = 10f
                },
                new()
                {
                    Number = 5,
                    Code = "TC005",
                    Label = "Speed 50 rpm",
                    Description = "ENCOS servo speed control at 50 rpm.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.SpeedControl,
                    TargetSpeed = 50f,
                    TargetTorque = 10f
                },
                new()
                {
                    Number = 6,
                    Code = "TC006",
                    Label = "Current 5 A",
                    Description = "ENCOS current control at 5 A.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.CurrentControl,
                    TargetTorque = 5f
                },
                new()
                {
                    Number = 7,
                    Code = "TC007",
                    Label = "Torque 5 Nm",
                    Description = "ENCOS torque control at 5 Nm.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.TorqueControl,
                    TargetTorque = 5f
                },
                new()
                {
                    Number = 8,
                    Code = "TC008",
                    Label = "Brake Release",
                    Description = "ENCOS electromagnetic brake release command.",
                    IntervalMs = 2,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.BrakeRelease
                }
            };
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
                    ? $"CAN connected — Protocol: {SelectedProtocol}  Port: {SelectedPort}  Baud: {SelectedBaud}  MotorID: {Config.MotorId}"
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
                        await TryRunRealTestCaseAsync(step.TestCaseNumber, step.Label);
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
        private async void OnRunTestCase(int number, string label)
        {
            if (!IsConnected)
            {
                AppendLog($"[TC{number} {label}] ERROR — CAN not connected. Connect first.");
                return;
            }

            try
            {
                if (await TryRunRealTestCaseAsync(number, label))
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
            AppendResult($"[TC{number} {label}] RESULT → {result}");
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
            CanFrameSpec frame = SelectedProtocol == MotorProtocolKind.Encos
                ? EncosMotorControl.BuildHybridControl(
                    m.DeviceId,
                    positionRad: (float)m.QCmd,
                    speedRadPerSec: (float)m.DqCmd,
                    torqueNm: (float)m.TauCmd,
                    kp: (float)m.Kp,
                    kd: (float)m.Kd)
                : AsExtendedFrame(RsMotorControl.ControlMotor(
                    m.DeviceId,
                    m.Profile,
                    torque: (float)m.TauCmd,
                    position: (float)m.QCmd,
                    speed: (float)m.DqCmd,
                    kp: (float)m.Kp,
                    kd: (float)m.Kd));

            return FormatCanFrame(frame);
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
            var connected = Model.Connect(SelectedCanBitrate, rawLogPath, Config.UseCanFd, out var statusMessage);
            IsConnected = connected;

            var message = connected
                ? $"CAN connected - Protocol: {SelectedProtocol}  Device: {Model.SelectedCan?.DisplayName ?? "unknown"}  CAN: {SelectedCanBitrate} kbps  MotorID: {Config.MotorId}. {statusMessage}"
                : $"CAN connect failed - {statusMessage}";

            AppendLog(message);
            LogHelper.Debug(message);
            return true;
        }

        private async Task<bool> TryRunRealTestCaseAsync(int number, string label)
        {
            var def = AvailableTestCases.FirstOrDefault(tc => tc.Number == number);
            if (def == null) return false;

            if (SelectedProtocol == MotorProtocolKind.Robstride)
                return await TryRunRobstrideTestCaseAsync(def, number, label);

            return await TryRunEncosTestCaseAsync(def, number, label);
        }

        private async Task<bool> TryRunRobstrideTestCaseAsync(TestCaseDefinition def, int number, string label)
        {
            if (def.RsCommand == RsCommandType.GetId)
                return await ScanAllMotorIdsAsync(number, label);

            var frame = BuildRsFrame(def);
            if (frame is null)
                return false;

            if (!Model.SendFrame(frame.Value, out var sendMessage))
            {
                AppendLog($"[TC{number} {label}] SEND ERROR - {sendMessage}");
                return true;
            }

            var txFrame = FormatCanFrame(frame.Value);
            AppendLog($"[TC{number} {label}] SEND  {txFrame}");
            LogHelper.Debug($"TC{number} TX: {txFrame}");
            return true;
        }

        private Task<bool> TryRunEncosTestCaseAsync(TestCaseDefinition def, int number, string label)
        {
            var frame = BuildEncosFrame(def);
            if (frame is null)
                return Task.FromResult(false);

            if (!Model.SendFrame(frame.Value, out var sendMessage))
            {
                AppendLog($"[TC{number} {label}] SEND ERROR - {sendMessage}");
                return Task.FromResult(true);
            }

            var txFrame = FormatCanFrame(frame.Value);
            AppendLog($"[TC{number} {label}] SEND  {txFrame}");
            LogHelper.Debug($"TC{number} TX: {txFrame}");
            return Task.FromResult(true);
        }

        private async Task<bool> ScanAllMotorIdsAsync(int number, string label)
        {
            AppendLog($"[TC{number} {label}] SCAN START 0x01 -> 0x7F");

            var discovered = new HashSet<byte>();
            var snapshot = Model.GetCanMessages();
            var lastSeenTime = snapshot.Count > 0 ? snapshot.Max(m => m.time) : -1.0;

            for (byte id = 0x01; id <= 0x7F; id++)
            {
                var frame = AsExtendedFrame(RsMotorControl.GetIdMotor(id));
                if (!Model.SendFrame(frame, out var sendMessage))
                {
                    AppendLog($"[TC{number} {label}] SEND ERROR - ID 0x{id:X2}: {sendMessage}");
                    continue;
                }

                await Task.Delay(2);
                lastSeenTime = DrainScanResponses(lastSeenTime, discovered, number, label);
            }

            await Task.Delay(30);
            lastSeenTime = DrainScanResponses(lastSeenTime, discovered, number, label);

            if (discovered.Count == 0)
            {
                AppendLog($"[TC{number} {label}] SCAN DONE - no motor responded.");
            }
            else
            {
                var ids = string.Join(", ", discovered.OrderBy(id => id).Select(id => $"0x{id:X2}"));
                AppendLog($"[TC{number} {label}] SCAN DONE - found: {ids}");
            }

            return true;
        }

        private double DrainScanResponses(double lastSeenTime, ISet<byte> discovered, int number, string label)
        {
            var newFrames = Model.GetCanMessages()
                .Where(m => m.status == 0 && m.time > lastSeenTime)
                .OrderBy(m => m.time)
                .ToList();

            if (newFrames.Count == 0)
                return lastSeenTime;

            foreach (var raw in newFrames)
            {
                var entry = new RxFrameEntry
                {
                    Timestamp = DateTime.Now,
                    Channel = raw.ch,
                    Status = raw.status,
                    Data = raw.data ?? Array.Empty<byte>()
                };

                if (entry.MotorId is not byte motorId)
                    continue;

                if (!discovered.Add(motorId))
                    continue;

                var profile = RsMotorProfileMap.Resolve(motorId);
                AppendLog($"[TC{number} {label}] FOUND ID=0x{motorId:X2} | Profile={profile.Name} | CH={entry.Channel}");
            }

            return newFrames.Max(m => m.time);
        }

        private CanFrameSpec? BuildRsFrame(TestCaseDefinition def)
        {
            return def.RsCommand switch
            {
                RsCommandType.GetId   => AsExtendedFrame(RsMotorControl.GetIdMotor(DeviceId)),
                RsCommandType.Enable  => AsExtendedFrame(RsMotorControl.EnableMotor(DeviceId)),
                RsCommandType.Disable => AsExtendedFrame(RsMotorControl.DisableMotor(DeviceId)),
                RsCommandType.SetZero => AsExtendedFrame(RsMotorControl.SetZeroPosition(DeviceId)),
                RsCommandType.Control => BuildControlFrame(def),
                _ => null
            };
        }

        private CanFrameSpec? BuildEncosFrame(TestCaseDefinition def)
        {
            return def.RsCommand switch
            {
                RsCommandType.QueryCanId => EncosMotorControl.QueryCanId(),
                RsCommandType.SetZero => EncosMotorControl.SetZeroPosition(DeviceId16),
                RsCommandType.Control => BuildEncosHybridFrame(def),
                RsCommandType.PositionControl => EncosMotorControl.BuildServoPositionControl(DeviceId16, def.TargetPosition, def.TargetSpeed, def.TargetTorque),
                RsCommandType.SpeedControl => EncosMotorControl.BuildServoSpeedControl(DeviceId16, def.TargetSpeed, def.TargetTorque),
                RsCommandType.CurrentControl => EncosMotorControl.BuildCurrentControl(DeviceId16, def.TargetTorque),
                RsCommandType.TorqueControl => EncosMotorControl.BuildTorqueControl(DeviceId16, def.TargetTorque),
                RsCommandType.BrakeRelease => EncosMotorControl.BuildElectromagneticBrake(DeviceId16, release: true),
                _ => null
            };
        }

        private CanFrameSpec BuildControlFrame(TestCaseDefinition def)
        {
            var profile = RsMotorProfileMap.Resolve(DeviceId);
            var speed = def.UseResolvedProfileMaxSpeed ? profile.VMax : def.TargetSpeed;

            return AsExtendedFrame(RsMotorControl.ControlMotor(
                DeviceId,
                profile,
                torque: def.TargetTorque,
                position: def.TargetPosition,
                speed: speed,
                kp: def.TargetKp,
                kd: def.TargetKd));
        }

        private CanFrameSpec BuildEncosHybridFrame(TestCaseDefinition def)
        {
            var speed = def.UseResolvedProfileMaxSpeed
                ? EncosMotorControl.HybridSpeedMaxRadPerSec
                : def.TargetSpeed;

            return EncosMotorControl.BuildHybridControl(
                DeviceId16,
                positionRad: def.TargetPosition,
                speedRadPerSec: speed,
                torqueNm: def.TargetTorque,
                kp: def.TargetKp,
                kd: def.TargetKd);
        }

        private bool TrySendMotorFrame(MotorChannelModel m)
        {
            CanFrameSpec frame = SelectedProtocol == MotorProtocolKind.Encos
                ? EncosMotorControl.BuildHybridControl(
                    m.DeviceId,
                    positionRad: (float)m.QCmd,
                    speedRadPerSec: (float)m.DqCmd,
                    torqueNm: (float)m.TauCmd,
                    kp: (float)m.Kp,
                    kd: (float)m.Kd)
                : AsExtendedFrame(RsMotorControl.ControlMotor(
                    m.DeviceId,
                    m.Profile,
                    torque: (float)m.TauCmd,
                    position: (float)m.QCmd,
                    speed: (float)m.DqCmd,
                    kp: (float)m.Kp,
                    kd: (float)m.Kd));

            if (!Model.SendFrame(frame, out var sendMessage))
            {
                AppendLog($"[{m.Label}] SEND ERROR - {sendMessage}");
                return true;
            }

            var frameText = FormatCanFrame(frame);
            AppendLog($"[{m.Label}] SEND  {frameText}");
            LogHelper.Debug($"{m.Label} TX: {frameText}");
            return true;
        }

        private bool TrySendAllMotors()
        {
            int sent = 0;

            foreach (var motor in Motors)
            {
                if (!motor.Enabled) continue;

                CanFrameSpec frame = SelectedProtocol == MotorProtocolKind.Encos
                    ? EncosMotorControl.BuildHybridControl(
                        motor.DeviceId,
                        positionRad: (float)motor.QCmd,
                        speedRadPerSec: (float)motor.DqCmd,
                        torqueNm: (float)motor.TauCmd,
                        kp: (float)motor.Kp,
                        kd: (float)motor.Kd)
                    : AsExtendedFrame(RsMotorControl.ControlMotor(
                        motor.DeviceId,
                        motor.Profile,
                        torque: (float)motor.TauCmd,
                        position: (float)motor.QCmd,
                        speed: (float)motor.DqCmd,
                        kp: (float)motor.Kp,
                        kd: (float)motor.Kd));

                if (!Model.SendFrame(frame, out var sendMessage))
                {
                    AppendLog($"[{motor.Label}] SEND ERROR - {sendMessage}");
                    continue;
                }

                var frameText = FormatCanFrame(frame);
                AppendLog($"[{motor.Label}] SEND  {frameText}");
                LogHelper.Debug($"{motor.Label} TX: {frameText}");
                sent++;
            }

            AppendLog($"[SendAll] {sent} motor(s) sent.");
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

        private ushort DeviceId16 => SelectedProtocol == MotorProtocolKind.Encos
            ? EncosMotorControl.ParseDeviceId(Config.MotorId)
            : RsMotorControl.ParseDeviceId(Config.MotorId);

        private byte DeviceId => (byte)DeviceId16;

        //private (string CanId, byte[] Payload) EnableMotor()   => RsMotorControl.EnableMotor(DeviceId);
        //private (string CanId, byte[] Payload) DisableMotor()  => RsMotorControl.DisableMotor(DeviceId);
        //private (string CanId, byte[] Payload) SetZeroPos()    => RsMotorControl.SetZeroPosition(DeviceId);

        private static CanFrameSpec AsExtendedFrame((string CanId, byte[] Payload) frame)
            => new(frame.CanId, frame.Payload, true);

        private static string FormatCanFrame(CanFrameSpec frame)
        {
            string idType = frame.IsExtendedId ? "EXT" : "STD";
            return $"{idType} ID={frame.CanId}  DLC={frame.Payload.Length}  Data=[{string.Join(" ", frame.Payload.Select(b => b.ToString("X2")))}]";
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

            if (!Model.SendMessage(canId, payload, CurrentProtocolUsesExtendedId, out var sendMessage))
            {
                RawSendStatus = sendMessage;
                AppendLog($"[RawFrame] SEND ERROR - {sendMessage}");
                return;
            }

            var frame = FormatCanFrame(new CanFrameSpec(canId, payload, CurrentProtocolUsesExtendedId));
            AppendLog($"[RawFrame] SEND  {frame}");
            LogHelper.Debug($"Raw TX: {frame}");
            RawSendStatus = "Frame sent.";
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
            NewTargetPosition = 0f;
            NewTargetSpeed = 0f;
            NewTargetTorque = 0f;
            NewTargetKp = (float)Config.DefaultKp;
            NewTargetKd = (float)Config.DefaultKd;
        }

        private void SaveNewConfig()
        {
            if (string.IsNullOrWhiteSpace(NewTestCaseDescription))
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
                CanPayLoad = string.Empty,
                ExpectedRespone = string.Empty,
                TimeoutMs = NewTimeoutMs,
                DefaultSendMode = NewDefaultSendMode,
                IntervalMs = NewIntervalMs,
                IsbuiltIn = false,
                RsCommand = RsCommandType.Control,
                TargetPosition = NewTargetPosition,
                TargetSpeed = NewTargetSpeed,
                TargetTorque = NewTargetTorque,
                TargetKp = NewTargetKp,
                TargetKd = NewTargetKd
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
                        SelectedProtocol = cfg.Protocol;

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
        public void AppendResult(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}]  {message}";
            TestResultEntries.Insert(0, entry);
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
            _lastRxTime  = -1.0;
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
                    // Use time-based filtering so we work correctly with both
                    // accumulating lists and fixed-size rolling buffers.
                    var newFrames = messages
                        .Where(m => m.time > _lastRxTime)
                        .ToList();
                    if (newFrames.Count > 0)
                    {
                        _lastRxTime  = newFrames.Max(m => m.time);
                        _lastRxCount = messages.Count;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var raw in newFrames)
                            {
                                if (raw.status != 0)
                                    continue;

                                var entry = new RxFrameEntry
                                {
                                    Timestamp = DateTime.Now,
                                    Channel   = raw.ch,
                                    Status    = raw.status,
                                    Data      = raw.data ?? Array.Empty<byte>()
                                };
                                var payload = entry.PayloadData;

                                RxFrames.Insert(0, entry);
                                if (RxFrames.Count > 1000)
                                    RxFrames.RemoveAt(RxFrames.Count - 1);

                                if (SelectedProtocol == MotorProtocolKind.Encos)
                                {
                                    HandleEncosRxFrame(entry, payload);
                                }
                                else if (entry.MotorId is byte motorId && payload.Length >= 8)
                                {
                                    // Motor ID is from context — match by channel (ch = DeviceId)
                                    var motor = Motors.FirstOrDefault(m => m.DeviceId == motorId);
                                    var profile = motor?.Profile ?? RsMotorProfileMap.Resolve(motorId);
                                    var fb = RsMotorControl.DecodeFeedback(motorId, payload, profile);

                                    if (motor != null)
                                    {
                                        motor.Q           = Math.Round(fb.Angle,       4);
                                        motor.Dq          = Math.Round(fb.Speed,       4);
                                        motor.Tau         = Math.Round(fb.Torque,      4);
                                        motor.Temperature = Math.Round(fb.Temperature, 2);
                                    }

                                    AppendLog($"[RX] {entry.CanIdText} | CH={entry.Channel} | " +
                                              $"Angle={fb.Angle:F4} rad  Speed={fb.Speed:F4} rad/s  " +
                                              $"Torque={fb.Torque:F4} Nm  Temp={fb.Temperature:F1}°C");
                                    //AppendLog($"[RY] {entry.DataLight}");
                                }
                                else
                                {
                                    AppendLog($"[RX] CH={entry.Channel}  {entry.CanIdText}  {entry.DataHex}");
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

        private void HandleRobstrideRxFrame(RxFrameEntry entry, byte[] payload, byte motorId)
        {
            var motor = Motors.FirstOrDefault(m => m.DeviceId == motorId);
            var profile = motor?.Profile ?? RsMotorProfileMap.Resolve(motorId);
            var fb = RsMotorControl.DecodeFeedback(motorId, payload, profile);

            if (motor != null)
            {
                motor.Q           = Math.Round(fb.Angle, 4);
                motor.Dq          = Math.Round(fb.Speed, 4);
                motor.Tau         = Math.Round(fb.Torque, 4);
                motor.Temperature = Math.Round(fb.Temperature, 2);
            }

            AppendLog($"[RX] {entry.CanIdText} | CH={entry.Channel} | Angle={fb.Angle:F4} rad  Speed={fb.Speed:F4} rad/s  Torque={fb.Torque:F4} Nm  Temp={fb.Temperature:F1}C");
        }

        private void HandleEncosRxFrame(RxFrameEntry entry, byte[] payload)
        {
            if (entry.DecodedRawId is not uint rawId)
            {
                AppendLog($"[RX] CH={entry.Channel}  {entry.CanIdText}  {entry.DataHex}");
                return;
            }

            if (!EncosMotorControl.TryDecodeReply(rawId, payload, out var reply))
            {
                AppendLog($"[RX] CH={entry.Channel}  {entry.CanIdText}  {entry.DataHex}");
                return;
            }

            if (reply.Feedback is { } feedback)
            {
                if (feedback.MotorId <= byte.MaxValue)
                {
                    var motor = Motors.FirstOrDefault(m => m.DeviceId == (byte)feedback.MotorId);
                    if (motor != null)
                    {
                        motor.Q           = Math.Round(feedback.PositionRad, 4);
                        motor.Dq          = Math.Round(feedback.SpeedRadPerSec, 4);
                        motor.Tau         = Math.Round(feedback.EffortValue, 4);
                        motor.Temperature = Math.Round(feedback.TemperatureC, 2);
                        motor.ErrorCode   = feedback.ErrorCode;
                    }
                }

                AppendLog($"[RX] {entry.CanIdText} | CH={entry.Channel} | {reply.Summary}");
                return;
            }

            AppendLog($"[RX] {entry.CanIdText} | CH={entry.Channel} | {reply.Summary}");
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
