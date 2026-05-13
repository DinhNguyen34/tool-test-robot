using Common.Core.Helpers;
using Common.Core.Telemetry;
using Microsoft.Win32;
using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Canopen;
using ModuleMotor.Cia402.Core;
using ModuleMotor.Cia402.Ethercat;
using ModuleMotor.Cia402.Ethercat.Soem;
using ModuleMotor.Cia402.Models;
using ModuleMotor.Controllers;
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
    public class MotorViewModel : BindableBase, ITelemetrySource
    {
        private const int N_MOTORS = 8;
        private const int EncosKeepAliveIntervalMs = 100;
        private const int Cia402DefaultPollIntervalMs = 100;
        private const int Cia402MinimumPdoCycleMs = 1;
        private readonly IRegionManager _regionManager;
        private readonly ITelemetryPublisher _telemetryPublisher;

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
            set
            {
                if (!SetProperty(ref _config, value))
                    return;

                RaisePropertyChanged(nameof(EthercatInterface));
                RaisePropertyChanged(nameof(Cia402CountsPerRevolution));
            }
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
                if (value != MotorProtocolKind.Encos)
                    StopEncosKeepAlive(log: false);
                if (value != MotorProtocolKind.Cia402Canopen && value != MotorProtocolKind.Cia402Ethercat)
                    StopCia402CyclicStreaming(log: false);
                if (value != MotorProtocolKind.Cia402Canopen && _cia402Adapter != null)
                    _ = DisconnectCia402Async();
                if (value != MotorProtocolKind.Cia402Ethercat && _soemMaster != null)
                    _ = DisconnectEthercatAsync();
                ResetRawFrameDefaults();
                RebuildBuiltInTestCases();
                BuiltSteps.Clear();
                SelectedAvailableTestCase = null;
                SelectedQuickLibraryTestCase = null;
                RaisePropertyChanged(nameof(CurrentProtocolUsesExtendedId));
                RaisePropertyChanged(nameof(ConnectLabel));
                RaisePropertyChanged(nameof(SendAllButtonText));
                RaisePropertyChanged(nameof(ManualControlHint));
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
        public string ConnectLabel => SelectedProtocol == MotorProtocolKind.Cia402Ethercat
            ? (Cia402IsConnected ? "DISCONNECT EtherCAT" : "EtherCAT Connect")
            : (IsConnected       ? "DISCONNECT CAN"      : "CAN Connect");

        public string CanStatusText => IsConnected || Cia402IsConnected ? "CONNECTED" : "DISCONNECTED";

        // ── Live RX ────────────────────────────────────────────────────────────
        public ObservableCollection<RxFrameEntry> RxFrames { get; } = new();
        private CancellationTokenSource? _rxCts;
        private CancellationTokenSource? _encosKeepAliveCts;
        private int    _lastRxCount;
        private double _lastRxTime = -1.0;
        private bool _isEncosKeepAliveActive;
        private string _encosKeepAliveDescription = string.Empty;

        private string _rxStatusText = "Not receiving";
        public string RxStatusText
        {
            get => _rxStatusText;
            set => SetProperty(ref _rxStatusText, value);
        }

        public bool IsEncosKeepAliveActive
        {
            get => _isEncosKeepAliveActive;
            private set
            {
                if (!SetProperty(ref _isEncosKeepAliveActive, value))
                    return;

                RaisePropertyChanged(nameof(SendAllButtonText));
                RaisePropertyChanged(nameof(ManualControlHint));
            }
        }

        public string SendAllButtonText => SelectedProtocol == MotorProtocolKind.Encos && IsEncosKeepAliveActive
            ? "Stop ENCOS"
            : "Send All";

        public string ManualControlHint => SelectedProtocol == MotorProtocolKind.Encos
            ? IsEncosKeepAliveActive
                ? $"ENCOS keepalive is active for {_encosKeepAliveDescription}. Commands refresh every {EncosKeepAliveIntervalMs} ms."
                : "ENCOS control commands stop after 500 ms without a follow-up frame. Send buttons keep them alive automatically."
            : "Use Send or Send All to transmit the current command set.";

        public DelegateCommand ClearRxCommand { get; private set; } = null!;

        // ── CiA 402 ────────────────────────────────────────────────────────────
        private IDriveController? _cia402Controller;
        private CanopenCia402Adapter? _cia402Adapter;   // non-null only for CANopen
        private SoemMaster? _soemMaster;                 // non-null only for EtherCAT
        private ICia402ProcessDataCapabilities? _cia402ProcessDataCapabilities;
        private CancellationTokenSource? _cia402PollCts;
        private readonly SemaphoreSlim _cia402IoLock = new(1, 1);
        private readonly SemaphoreSlim _ethercatLifecycleLock = new(1, 1);
        public IReadOnlyList<Cia402OperationMode> Cia402OperationModes { get; } = Enum.GetValues<Cia402OperationMode>();

        private bool _cia402IsConnected;
        public bool Cia402IsConnected
        {
            get => _cia402IsConnected;
            private set
            {
                if (!SetProperty(ref _cia402IsConnected, value))
                    return;
                RaisePropertyChanged(nameof(ConnectLabel));
                RaisePropertyChanged(nameof(CanStatusText));
                RaisePropertyChanged(nameof(Cia402CanEditPdo));
                RaisePropertyChanged(nameof(Cia402SetpointHint));
                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

        public bool Cia402CanEditPdo => !Cia402IsConnected;

        private bool _cia402ProcessDataActive;
        public bool Cia402ProcessDataActive
        {
            get => _cia402ProcessDataActive;
            private set
            {
                if (!SetProperty(ref _cia402ProcessDataActive, value))
                    return;

                RaisePropertyChanged(nameof(Cia402SetpointHint));
                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

        private string _cia402StatusText = "—";
        public string Cia402StatusText
        {
            get => _cia402StatusText;
            set => SetProperty(ref _cia402StatusText, value);
        }

        private double _cia402Position;
        public double Cia402Position
        {
            get => _cia402Position;
            set => SetProperty(ref _cia402Position, value);
        }

        private double _cia402Velocity;
        public double Cia402Velocity
        {
            get => _cia402Velocity;
            set => SetProperty(ref _cia402Velocity, value);
        }

        private double _cia402Torque;
        public double Cia402Torque
        {
            get => _cia402Torque;
            set => SetProperty(ref _cia402Torque, value);
        }

        private ushort _cia402ErrorCode;
        public ushort Cia402ErrorCode
        {
            get => _cia402ErrorCode;
            set => SetProperty(ref _cia402ErrorCode, value);
        }

        private ushort _cia402StatuswordRaw;
        public ushort Cia402StatuswordRaw
        {
            get => _cia402StatuswordRaw;
            set => SetProperty(ref _cia402StatuswordRaw, value);
        }

        private bool _cia402ReadyToSwitchOn;
        public bool Cia402ReadyToSwitchOn
        {
            get => _cia402ReadyToSwitchOn;
            set => SetProperty(ref _cia402ReadyToSwitchOn, value);
        }

        private bool _cia402SwitchedOn;
        public bool Cia402SwitchedOn
        {
            get => _cia402SwitchedOn;
            set => SetProperty(ref _cia402SwitchedOn, value);
        }

        private bool _cia402OperationEnabled;
        public bool Cia402OperationEnabled
        {
            get => _cia402OperationEnabled;
            set => SetProperty(ref _cia402OperationEnabled, value);
        }

        private bool _cia402Fault;
        public bool Cia402Fault
        {
            get => _cia402Fault;
            set => SetProperty(ref _cia402Fault, value);
        }

        private bool _cia402VoltageEnabled;
        public bool Cia402VoltageEnabled
        {
            get => _cia402VoltageEnabled;
            set => SetProperty(ref _cia402VoltageEnabled, value);
        }

        private bool _cia402QuickStopNotActive;
        public bool Cia402QuickStopNotActive
        {
            get => _cia402QuickStopNotActive;
            set => SetProperty(ref _cia402QuickStopNotActive, value);
        }

        private bool _cia402SwitchOnDisabled;
        public bool Cia402SwitchOnDisabled
        {
            get => _cia402SwitchOnDisabled;
            set => SetProperty(ref _cia402SwitchOnDisabled, value);
        }

        private bool _cia402Warning;
        public bool Cia402Warning
        {
            get => _cia402Warning;
            set => SetProperty(ref _cia402Warning, value);
        }

        private bool _cia402Remote;
        public bool Cia402Remote
        {
            get => _cia402Remote;
            set => SetProperty(ref _cia402Remote, value);
        }

        private bool _cia402TargetReached;
        public bool Cia402TargetReached
        {
            get => _cia402TargetReached;
            set => SetProperty(ref _cia402TargetReached, value);
        }

        private bool _cia402InternalLimitActive;
        public bool Cia402InternalLimitActive
        {
            get => _cia402InternalLimitActive;
            set => SetProperty(ref _cia402InternalLimitActive, value);
        }

        private bool _cia402SetPointAcknowledge;
        public bool Cia402SetPointAcknowledge
        {
            get => _cia402SetPointAcknowledge;
            set => SetProperty(ref _cia402SetPointAcknowledge, value);
        }

        private bool _cia402FollowingError;
        public bool Cia402FollowingError
        {
            get => _cia402FollowingError;
            set => SetProperty(ref _cia402FollowingError, value);
        }

        private double _cia402TargetPosition;
        public double Cia402TargetPosition
        {
            get => _cia402TargetPosition;
            set => SetProperty(ref _cia402TargetPosition, value);
        }

        private double _cia402TargetVelocity;
        public double Cia402TargetVelocity
        {
            get => _cia402TargetVelocity;
            set => SetProperty(ref _cia402TargetVelocity, value);
        }

        public int Cia402CountsPerRevolution
        {
            get => Config.Cia402CountsPerRevolution;
            set
            {
                int normalized = Math.Max(1, value);
                if (Config.Cia402CountsPerRevolution == normalized)
                    return;

                Config.Cia402CountsPerRevolution = normalized;
                RaisePropertyChanged();
            }
        }

        private int _cia402TargetTorque;
        public int Cia402TargetTorque
        {
            get => _cia402TargetTorque;
            set => SetProperty(ref _cia402TargetTorque, value);
        }

        private Cia402OperationMode _selectedCia402Mode = Cia402OperationMode.ProfilePosition;
        public Cia402OperationMode SelectedCia402Mode
        {
            get => _selectedCia402Mode;
            set
            {
                if (!SetProperty(ref _selectedCia402Mode, value))
                    return;

                StopCia402CyclicStreaming(log: false);
                RaisePropertyChanged(nameof(Cia402SetpointHint));
            }
        }

        private bool _cia402ProfileImmediateChange;
        public bool Cia402ProfileImmediateChange
        {
            get => _cia402ProfileImmediateChange;
            set => SetProperty(ref _cia402ProfileImmediateChange, value);
        }

        private int _cia402ProfileAckTimeoutMs = 5000;
        public int Cia402ProfileAckTimeoutMs
        {
            get => _cia402ProfileAckTimeoutMs;
            set => SetProperty(ref _cia402ProfileAckTimeoutMs, value);
        }

        private int _cia402ProfileVelocity = 5566;
        public int Cia402ProfileVelocity
        {
            get => _cia402ProfileVelocity;
            set => SetProperty(ref _cia402ProfileVelocity, value);
        }

        private int _cia402ProfileAcceleration = 5566;
        public int Cia402ProfileAcceleration
        {
            get => _cia402ProfileAcceleration;
            set => SetProperty(ref _cia402ProfileAcceleration, value);
        }

        private int _cia402ProfileDeceleration = 5566;
        public int Cia402ProfileDeceleration
        {
            get => _cia402ProfileDeceleration;
            set => SetProperty(ref _cia402ProfileDeceleration, value);
        }

        private bool _cia402CyclicStreamingActive;
        public bool Cia402CyclicStreamingActive
        {
            get => _cia402CyclicStreamingActive;
            private set
            {
                if (!SetProperty(ref _cia402CyclicStreamingActive, value))
                    return;

                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

        private string _cia402CyclicStreamingDescription = "Idle";
        public string Cia402CyclicStreamingDescription
        {
            get => _cia402CyclicStreamingDescription;
            private set
            {
                if (!SetProperty(ref _cia402CyclicStreamingDescription, value))
                    return;

                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

        private Cia402OperationMode? _cia402CyclicStreamingMode;
        private double _cia402CyclicPositionCommandCounts;
        private double _cia402CyclicPositionVelocityCountsPerSecond;
        private int _cia402CyclicVelocityCommandCountsPerSecond;
        private short _cia402CyclicTorqueCommand;

        public string Cia402CyclicStreamingText => Cia402CyclicStreamingActive && _cia402CyclicStreamingMode is { } activeMode
            ? $"Streaming {activeMode} via PDO every {Cia402PdoCycleMs} ms. Targets are sent before each SYNC."
            : Cia402IsConnected && !Cia402ProcessDataActive
                ? "PDO stream idle. Disconnect and reconnect with Enable PDO on to use CSV/CST/CSP."
                : Cia402EnablePdo
                ? "PDO stream idle. Use CSV/CST/CSP commands to start cyclic updates."
                : "PDO stream idle. Enable PDO to use CSV/CST/CSP.";

        public string Cia402SetpointHint => SelectedCia402Mode switch
        {
            Cia402OperationMode.ProfilePosition => "Profile Position sends one acknowledged set-point.",
            Cia402OperationMode.ProfileVelocity => "Profile Velocity writes Target Velocity after mode, ramp, state, and error checks.",
            Cia402OperationMode.ProfileTorque => "Profile Torque writes Target Torque once using the current transport.",
            Cia402OperationMode.CyclicSynchronousPosition => Cia402ProcessDataActive
                ? "CSP ramps cyclic PDO position targets from actual position to the requested target."
                : Cia402IsConnected ? "CSP requires reconnect with Enable PDO on." : "CSP requires Enable PDO.",
            Cia402OperationMode.CyclicSynchronousVelocity => Cia402ProcessDataActive
                ? "CSV ramps cyclic PDO velocity targets using Profile Acceleration."
                : Cia402IsConnected ? "CSV requires reconnect with Enable PDO on." : "CSV requires Enable PDO.",
            Cia402OperationMode.CyclicSynchronousTorque => Cia402ProcessDataActive
                ? "CST ramps cyclic PDO torque targets instead of stepping immediately."
                : Cia402IsConnected ? "CST requires reconnect with Enable PDO on." : "CST requires Enable PDO.",
            _ => "Choose a CiA402 mode, then send the matching setpoint."
        };

        public DelegateCommand Cia402FaultResetCommand   { get; }
        public DelegateCommand Cia402ShutdownCommand     { get; }
        public DelegateCommand Cia402SwitchOnCommand     { get; }
        public DelegateCommand Cia402EnableCommand       { get; }
        public DelegateCommand Cia402DisableCommand      { get; }
        public DelegateCommand Cia402QuickStopCommand    { get; }
        public DelegateCommand Cia402SetModeCommand      { get; }
        public DelegateCommand Cia402SyncPositionCommand { get; }
        public DelegateCommand Cia402MoveCommand         { get; }
        public DelegateCommand Cia402SetVelocityCommand  { get; }
        public DelegateCommand Cia402SetTorqueCommand    { get; }
        public DelegateCommand Cia402StopCyclicCommand   { get; }
        public DelegateCommand ScanEthercatAdaptersCommand { get; }

        // ── Port selection ─────────────────────────────────────────────────────
        public ObservableCollection<string> AvailablePorts { get; } = new();
        public ObservableCollection<SoemNetworkAdapter> EthercatAdapters { get; } = new();

        private SoemNetworkAdapter? _selectedEthercatAdapter;
        public SoemNetworkAdapter? SelectedEthercatAdapter
        {
            get => _selectedEthercatAdapter;
            set
            {
                if (!SetProperty(ref _selectedEthercatAdapter, value) || value == null)
                    return;

                EthercatInterface = value.Name;
            }
        }

        public string EthercatInterface
        {
            get => Config.EthercatInterface;
            set
            {
                value = NormalizeEthercatInterfaceName(value);

                if (Config.EthercatInterface == value)
                    return;

                Config.EthercatInterface = value;
                RaisePropertyChanged();
            }
        }

        public static IReadOnlyList<int> BaudRates { get; } = new[]
            {19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000};

        public static IReadOnlyList<int> CanBitratesKbps { get; } = new[]
            {50, 100, 120, 200, 250, 400, 500, 800, 1000, 1200, 1500, 2000};

        public bool CurrentProtocolUsesExtendedId => SelectedProtocol == MotorProtocolKind.Robstride;

        public bool Cia402EnablePdo
        {
            get => Config.Cia402EnablePdo;
            set
            {
                if (Config.Cia402EnablePdo == value)
                    return;

                if (Cia402IsConnected)
                {
                    AppendLog("[CiA402] Enable PDO can only be changed while disconnected. Disconnect and reconnect to change the PDO data path.");
                    RaisePropertyChanged();
                    return;
                }

                Config.Cia402EnablePdo = value;
                if (!value)
                    StopCia402CyclicStreaming(log: false);

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Cia402SetpointHint));
                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

        public int Cia402PdoCycleMs
        {
            get => Config.Cia402PdoCycleMs <= 0 ? 20 : Config.Cia402PdoCycleMs;
            set
            {
                int sanitized = Math.Max(Cia402MinimumPdoCycleMs, value);
                if (Config.Cia402PdoCycleMs == sanitized)
                    return;

                Config.Cia402PdoCycleMs = sanitized;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Cia402CyclicStreamingText));
            }
        }

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
        public DelegateCommand CanConnectCommand { get; }
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

        public DelegateCommand AddQuickLibraryStepCommand { get; }
        // ── Constructor ────────────────────────────────────────────────────────
        // ── ITelemetrySource ───────────────────────────────────────────────────
        string ITelemetrySource.ChannelPrefix => "motor";
        bool ITelemetrySource.IsActive => IsConnected || Cia402IsConnected;
        IEnumerable<(string Name, double Value)> ITelemetrySource.Sample()
        {
            if (Cia402IsConnected)
            {
                yield return ("cia402/position",   Cia402Position);
                yield return ("cia402/velocity",   Cia402Velocity);
                yield return ("cia402/torque",     Cia402Torque);
                yield return ("cia402/statusword", Cia402StatuswordRaw);
                yield return ("cia402/error_code", Cia402ErrorCode);
            }

            if (IsConnected)
            {
                foreach (MotorChannelModel m in Motors)
                {
                    string p = m.Label.ToLowerInvariant(); // "m01" .. "m08"
                    yield return ($"{p}/position",    m.Q);
                    yield return ($"{p}/velocity",    m.Dq);
                    yield return ($"{p}/torque",      m.Tau);
                    yield return ($"{p}/temperature", m.Temperature);
                    yield return ($"{p}/error_code",  m.ErrorCode);
                }
            }
        }

        // ── Constructor ────────────────────────────────────────────────────────
        public MotorViewModel(IRegionManager regionManager, ITelemetryPublisher telemetryPublisher)
        {
            _regionManager = regionManager;
            _telemetryPublisher = telemetryPublisher;
            telemetryPublisher.Register(this);

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
            CanConnectCommand = new DelegateCommand(OnCanConnect);
            //RefreshPortsCommand = new DelegateCommand(RefreshPorts);
            SendMotorCommand = new DelegateCommand<MotorChannelModel>(OnSendMotor);
            SendAllMotorsCommand = new DelegateCommand(OnSendAllMotors);
            SaveNewConfigCommand = new DelegateCommand(SaveNewConfig);
            SendRawFrameCommand = new DelegateCommand(OnSendRawFrame);
            ClearRxCommand      = new DelegateCommand(() => { RxFrames.Clear(); _lastRxCount = 0; _lastRxTime = -1.0; });

            Cia402FaultResetCommand   = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new FaultResetDriveCommand()));
            Cia402ShutdownCommand     = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new ShutdownDriveCommand()));
            Cia402SwitchOnCommand     = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new SwitchOnDriveCommand()));
            Cia402EnableCommand       = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new EnableOperationDriveCommand()));
            Cia402DisableCommand      = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new DisableOperationDriveCommand()));
            Cia402QuickStopCommand    = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new QuickStopDriveCommand()));
            Cia402SetModeCommand      = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new SetModeDriveCommand(SelectedCia402Mode)));
            Cia402SyncPositionCommand = new DelegateCommand(() => ExecuteCia402StateMachineCommand(new SyncActualPositionToTargetDriveCommand()));
            Cia402MoveCommand         = new DelegateCommand(ExecuteCia402PositionCommand);
            Cia402SetVelocityCommand  = new DelegateCommand(ExecuteCia402VelocityCommand);
            Cia402SetTorqueCommand    = new DelegateCommand(ExecuteCia402TorqueCommand);
            Cia402StopCyclicCommand   = new DelegateCommand(() => StopCia402CyclicStreaming(), () => Cia402CyclicStreamingActive)
                .ObservesProperty(() => Cia402CyclicStreamingActive);
            ScanEthercatAdaptersCommand = new DelegateCommand(ScanEthercatAdapters);

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
                    IntervalMs = 100,
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
                    IntervalMs = 100,
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
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
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
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
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
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
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
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
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
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
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
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.BrakeRelease
                },
                new()
                {
                    Number = 9,
                    Code = "TC009",
                    Label = "Brake Engage",
                    Description = "ENCOS electromagnetic brake engage command.",
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.BrakeEngage
                },
                new()
                {
                    Number = 10,
                    Code = "TC010",
                    Label = "Hybrid Zero",
                    Description = "ENCOS hybrid control: hold zero position (KP=20, KD=5).",
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.Control,
                    TargetPosition = 0f,
                    TargetSpeed = 0f,
                    TargetTorque = 0f,
                    TargetKp = 20f,
                    TargetKd = 5f
                },
                new()
                {
                    Number = 11,
                    Code = "TC011",
                    Label = "Hybrid 90 deg",
                    Description = "ENCOS hybrid control: move to 90 deg (1.571 rad, KP=20, KD=5).",
                    IntervalMs = 100,
                    TimeoutMs = 5000,
                    DefaultSendMode = CanSendMode.Continuous,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.Control,
                    TargetPosition = 1.5708f,
                    TargetSpeed = 0f,
                    TargetTorque = 0f,
                    TargetKp = 20f,
                    TargetKd = 5f
                },
                new()
                {
                    Number = 12,
                    Code = "TC012",
                    Label = "Query Position",
                    Description = "ENCOS query current output position (query code 0x01).",
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.Query,
                    TargetTorque = 0x01
                },
                new()
                {
                    Number = 13,
                    Code = "TC013",
                    Label = "Query Speed",
                    Description = "ENCOS query current output speed (query code 0x02).",
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.Query,
                    TargetTorque = 0x02
                },
                new()
                {
                    Number = 14,
                    Code = "TC014",
                    Label = "Query Current",
                    Description = "ENCOS query current phase current (query code 0x03).",
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.Query,
                    TargetTorque = 0x03
                },
                new()
                {
                    Number = 15,
                    Code = "TC015",
                    Label = "Reset Motor ID",
                    Description = "ENCOS broadcast reset all motor IDs to factory default.",
                    IntervalMs = 100,
                    TimeoutMs = 1000,
                    DefaultSendMode = CanSendMode.SendOnce,
                    IsbuiltIn = true,
                    RsCommand = RsCommandType.ResetMotorId
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
            catch (Exception ex)
            {
                AppendLog($"Connect error: {ex.Message}");
                LogHelper.Exception(ex);
            }
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
                var def = AvailableTestCases.FirstOrDefault(tc => tc.Number == number);
                if (SelectedProtocol == MotorProtocolKind.Encos && def != null)
                {
                    if (def.DefaultSendMode == CanSendMode.Continuous)
                    {
                        if (await TryRunEncosTestCaseAsync(def, number, label))
                            StartEncosKeepAlive(
                                $"TC{number} {label}",
                                () => BuildEncosKeepAliveFrames(def));
                        return;
                    }

                    StopEncosKeepAlive("[ENCOS] Keepalive stopped for one-shot command.", log: IsEncosKeepAliveActive);
                }

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
            if (SelectedProtocol == MotorProtocolKind.Encos)
                StopEncosKeepAlive("[ENCOS] Keepalive stopped and all commands were zeroed.", log: IsEncosKeepAliveActive);
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
            if (!TrySendMotorFrame(m))
                return;

            if (SelectedProtocol == MotorProtocolKind.Encos)
                StartEncosKeepAlive(
                    $"manual control {m.Label}",
                    () => BuildEncosFramesForMotor(m));
        }

        private void OnSendAllMotors()
        {
            if (!IsConnected)
            {
                AppendLog("[SendAll] ERROR — CAN not connected.");
                return;
            }
            if (SelectedProtocol == MotorProtocolKind.Encos && IsEncosKeepAliveActive)
            {
                StopEncosKeepAlive("[ENCOS] Keepalive stopped by operator.");
                return;
            }

            if (!TrySendAllMotors())
                return;

            if (SelectedProtocol == MotorProtocolKind.Encos)
                StartEncosKeepAlive(
                    "manual control for enabled motors",
                    BuildEncosFramesForEnabledMotors);
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

        //-- disconnect CAN / EtherCAT -------
        private void DisconnectCAN()
        {
            StopEncosKeepAlive(log: false);

            if (_soemMaster != null)
            {
                _ = DisconnectEthercatAsync();
                return;
            }

            if (_cia402Adapter != null)
                _ = DisconnectCia402Async();

            if (IsConnected || Model.GetOpenStatus())
            {
                Model.Close();
                IsConnected = false;

                const string dismessage = "CAN disconnected";
                AppendLog(dismessage);
                LogHelper.Debug(dismessage);
            }
        }

        private bool HandleCanConnect()
        {
            // ── EtherCAT disconnect ───────────────────────────────────────────────
            if (SelectedProtocol == MotorProtocolKind.Cia402Ethercat || _soemMaster != null)
            {
                if (Cia402IsConnected || _soemMaster != null)
                {
                    StopEncosKeepAlive(log: false);
                    _ = DisconnectEthercatAsync();
                    return true;
                }

                // ── EtherCAT connect ──────────────────────────────────────────────
                if (!TryValidateEthercatSlaveIndex(out _))
                    return true;

                AppendLog("[EtherCAT] Connecting...");
                _ = ConnectEthercatAsync();
                return true;
            }

            // ── CAN disconnect ────────────────────────────────────────────────────
            if (IsConnected || Model.GetOpenStatus())
            {
                StopEncosKeepAlive(log: false);
                if (_cia402Adapter != null)
                    _ = DisconnectCia402Async();
                Model.Close();
                IsConnected = false;
                const string disconnectMessage = "CAN disconnected";
                AppendLog(disconnectMessage);
                LogHelper.Debug(disconnectMessage);
                return true;
            }

            // ── CAN connect validation ────────────────────────────────────────────
            if (SelectedProtocol == MotorProtocolKind.Cia402Canopen && !TryValidateCia402NodeId(out _))
                return true;

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

            if (connected && SelectedProtocol == MotorProtocolKind.Cia402Canopen)
                _ = ConnectCia402Async();

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
                RsCommandType.BrakeRelease  => EncosMotorControl.BuildElectromagneticBrake(DeviceId16, release: true),
                RsCommandType.BrakeEngage   => EncosMotorControl.BuildElectromagneticBrake(DeviceId16, release: false),
                RsCommandType.Query         => EncosMotorControl.BuildQuery(DeviceId16, (byte)def.TargetTorque),
                RsCommandType.ResetMotorId  => EncosMotorControl.ResetMotorId(),
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

        private IReadOnlyList<CanFrameSpec> BuildEncosFramesForMotor(MotorChannelModel motor)
            => new[] { BuildEncosHybridControlFrame(motor) };

        private IReadOnlyList<CanFrameSpec> BuildEncosFramesForEnabledMotors()
            => Motors
                .Where(motor => motor.Enabled)
                .Select(BuildEncosHybridControlFrame)
                .ToArray();

        private IReadOnlyList<CanFrameSpec> BuildEncosKeepAliveFrames(TestCaseDefinition def)
        {
            var frame = BuildEncosFrame(def);
            return frame is CanFrameSpec value
                ? new[] { value }
                : Array.Empty<CanFrameSpec>();
        }

        private CanFrameSpec BuildEncosHybridControlFrame(MotorChannelModel motor)
            => EncosMotorControl.BuildHybridControl(
                motor.DeviceId,
                positionRad: (float)motor.QCmd,
                speedRadPerSec: (float)motor.DqCmd,
                torqueNm: (float)motor.TauCmd,
                kp: (float)motor.Kp,
                kd: (float)motor.Kd);

        private void StartEncosKeepAlive(string description, Func<IReadOnlyList<CanFrameSpec>> frameFactory)
        {
            StopEncosKeepAlive(log: false);

            if (!IsConnected || SelectedProtocol != MotorProtocolKind.Encos)
                return;

            var cts = new CancellationTokenSource();
            _encosKeepAliveCts = cts;
            _encosKeepAliveDescription = description;
            IsEncosKeepAliveActive = true;
            AppendLog($"[ENCOS] Keepalive started - {description}. Refresh interval: {EncosKeepAliveIntervalMs} ms.");
            _ = RunEncosKeepAliveAsync(cts, frameFactory);
        }

        private async Task RunEncosKeepAliveAsync(CancellationTokenSource cts, Func<IReadOnlyList<CanFrameSpec>> frameFactory)
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(EncosKeepAliveIntervalMs, cts.Token).ConfigureAwait(false);

                    IReadOnlyList<CanFrameSpec> frames = Array.Empty<CanFrameSpec>();
                    await Application.Current.Dispatcher.InvokeAsync(() => frames = frameFactory());

                    if (frames.Count == 0)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (_encosKeepAliveCts == cts)
                                StopEncosKeepAlive("[ENCOS] Keepalive stopped - no ENCOS control frame is available.");
                        });
                        return;
                    }

                    foreach (var frame in frames)
                    {
                        if (!Model.SendFrame(frame, out var sendMessage))
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (_encosKeepAliveCts == cts)
                                    StopEncosKeepAlive($"[ENCOS] Keepalive send error - {sendMessage}");
                            });
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void StopEncosKeepAlive(string? reason = null, bool log = true)
        {
            var cts = _encosKeepAliveCts;
            if (cts == null)
                return;

            _encosKeepAliveCts = null;
            _encosKeepAliveDescription = string.Empty;
            IsEncosKeepAliveActive = false;
            cts.Cancel();
            cts.Dispose();

            if (log)
                AppendLog(reason ?? "[ENCOS] Keepalive stopped.");
        }

        private bool TrySendMotorFrame(MotorChannelModel m)
        {
            CanFrameSpec frame = SelectedProtocol == MotorProtocolKind.Encos
                ? BuildEncosHybridControlFrame(m)
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
                return false;
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
                    ? BuildEncosHybridControlFrame(motor)
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

            if (sent == 0)
            {
                AppendLog("[SendAll] No enabled motor was sent.");
                return false;
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

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                LogEntries.Insert(0, entry);
            else
                dispatcher.Invoke(() => LogEntries.Insert(0, entry));

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
            DisconnectCAN();
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
            int repeatCount = testCase.DefaultSendMode == CanSendMode.Continuous
                ? Math.Max(1, (int)Math.Ceiling(testCase.TimeoutMs / (double)Math.Max(1, testCase.IntervalMs)))
                : 1;

            BuiltSteps.Add(new BuiltTestStep
            {
                StepNo = BuiltSteps.Count + 1,
                TestCaseNumber = testCase.Number,
                Label = testCase.Label,
                SendMode = testCase.DefaultSendMode,
                IntervalMs = testCase.IntervalMs,
                TimeoutMs = testCase.TimeoutMs,
                RepeatCount = repeatCount,
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

        // ── CiA 402 connect / disconnect ───────────────────────────────────────
        private async Task ConnectCia402Async()
        {
            try
            {
                if (!TryValidateCia402NodeId(out byte nodeId))
                    return;

                var (controller, adapter) = DriveControllerFactory.CreateCanopenCia402Controller(
                    Model, nodeId, enablePdo: Cia402EnablePdo);

                _cia402Controller = controller;
                _cia402Adapter = adapter;
                _cia402ProcessDataCapabilities = Cia402EnablePdo ? adapter : null;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await adapter.OpenAsync(cts.Token);

                AppendLog($"[CiA402] NMT Start — Node ID=0x{nodeId:X2}  PDO={Cia402EnablePdo}");
                StartCia402PollLoop();
                Cia402ProcessDataActive = Cia402EnablePdo;
                Cia402IsConnected = true;
            }
            catch (Exception ex)
            {
                _cia402Controller = null;
                _cia402Adapter = null;
                _cia402ProcessDataCapabilities = null;
                Cia402ProcessDataActive = false;
                Cia402IsConnected = false;
                AppendLog($"[CiA402] Connect error: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        private async Task DisconnectCia402Async()
        {
            StopCia402PollLoop();
            var adapter = _cia402Adapter;
            _cia402Controller = null;
            _cia402Adapter = null;
            _cia402ProcessDataCapabilities = null;
            Cia402ProcessDataActive = false;

            if (adapter == null) return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await adapter.CloseAsync(cts.Token);
                AppendLog("[CiA402] NMT Stop — node stopped.");
            }
            catch (Exception ex)
            {
                AppendLog($"[CiA402] Disconnect error: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        // ── EtherCAT connect / disconnect ──────────────────────────────────────
        private void ScanEthercatAdapters()
        {
            try
            {
                IReadOnlyList<SoemNetworkAdapter> adapters = RefreshEthercatAdapters();
                if (EthercatAdapters.Count == 0)
                {
                    AppendLog("[EtherCAT] No Ethernet adapters were found from the local Windows network list.");
                    return;
                }

                AppendLog($"[EtherCAT] Found {EthercatAdapters.Count} adapter(s) from the local Windows network list.");
            }
            catch (DllNotFoundException ex)
            {
                AppendLog($"[EtherCAT] SOEM scan failed: {ex.Message}");
                LogHelper.Exception(ex);
            }

            catch (BadImageFormatException ex)
            {
                AppendLog($"[EtherCAT] SOEM scan failed: soem.dll architecture does not match the app. {ex.Message}");
                LogHelper.Exception(ex);
            }
            catch (Exception ex)
            {
                AppendLog($"[EtherCAT] SOEM scan failed: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        private IReadOnlyList<SoemNetworkAdapter> RefreshEthercatAdapters()
        {
            EthercatAdapters.Clear();

            IReadOnlyList<SoemNetworkAdapter> adapters = SoemNative.FindAdapters();
            foreach (SoemNetworkAdapter adapter in adapters)
                EthercatAdapters.Add(adapter);

            string configuredInterfaceName = NormalizeEthercatInterfaceName(Config.EthercatInterface);
            SoemNetworkAdapter? selected = null;

            if (_selectedEthercatAdapter != null)
            {
                selected = EthercatAdapters.FirstOrDefault(a =>
                    string.Equals(a.Name, _selectedEthercatAdapter.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null && !string.IsNullOrWhiteSpace(configuredInterfaceName))
            {
                selected = EthercatAdapters.FirstOrDefault(a =>
                    string.Equals(a.Name, configuredInterfaceName, StringComparison.OrdinalIgnoreCase));
            }

            selected ??= EthercatAdapters.FirstOrDefault();

            if (selected != null)
            {
                SelectedEthercatAdapter = selected;
            }
            else
            {
                _selectedEthercatAdapter = null;
                EthercatInterface = string.Empty;
                RaisePropertyChanged(nameof(SelectedEthercatAdapter));
            }

            return adapters;
        }

        private async Task ConnectEthercatAsync()
        {
            SoemMaster? master = null;
            try
            {
                await _ethercatLifecycleLock.WaitAsync();
                try
                {
                    if (_soemMaster != null || Cia402IsConnected)
                    {
                        AppendLog("[EtherCAT] Already connected.");
                        return;
                    }

                    if (!TryResolveEthercatInterface(out string interfaceName))
                        return;

                    if (!SoemNative.TryEnsureLoaded(out string nativeLoadError))
                    {
                        AppendLog($"[EtherCAT] {nativeLoadError}");
                        return;
                    }

                    if (SoemNative.TryGetNpcapAdminOnlyError(out string npcapPermissionError))
                    {
                        AppendLog($"[EtherCAT] {npcapPermissionError}");
                        return;
                    }

                    if (!TryValidateEthercatSlaveIndex(out ushort slaveIndex))
                        return;

                    const int ethercatMasterCycleMs = 1;
                    master = new SoemMaster(interfaceName, ethercatMasterCycleMs);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                    bool ethercatProcessDataEnabled = true;
                    // Reprogram the slave's RxPDO/TxPDO so 0x607A/0x60FF/0x6071 are always
                    // mapped — required for CSP/CSV/CST. Runs in PRE-OP before ConfigureIoMap
                    // freezes the IO image. Skipped when PDO is off (no cyclic data path).
                    Func<SoemMaster, CancellationToken, Task>? pdoConfigure = Cia402EnablePdo
                        ? async (m, c) =>
                        {
                            try
                            {
                                await ErobPdoConfigurator.ApplyDefaultMappingAsync(m, slaveIndex, c).ConfigureAwait(false);
                            }
                            catch (Exception mapEx)
                            {
                                AppendLog($"[eRob-PDO] Default PDO remap skipped: {mapEx.Message}");
                            }
                        }
                        : null;
                    await master.OpenAsync(
                        configurePdoMappingAsync: pdoConfigure,
                        ct: cts.Token,
                        enableProcessData: ethercatProcessDataEnabled);

                    if (slaveIndex > master.SlaveCount)
                    {
                        throw new InvalidOperationException(
                            $"Configured EtherCAT slave index {slaveIndex} is outside the detected slave range 1..{master.SlaveCount}.");
                    }


                    AppendLog($"[EtherCAT] Open — Interface={Config.EthercatInterface}  Slave={slaveIndex}  Slaves found={master.SlaveCount}  PDO={Cia402EnablePdo}");

                    if (!Cia402EnablePdo)
                        AppendLog("[EtherCAT] CiA402 state-machine commands require EtherCAT OP/PDO process data; cyclic setpoint streaming remains disabled by the Enable PDO checkbox.");

                    if (ethercatProcessDataEnabled)
                    {
                        // Give the cyclic thread a few ticks so the IO map reflects real PDO data.
                        await Task.Delay(50, cts.Token);
                        AppendLog(master.GetDiagnosticsReport());
                    }

                    ErobPdoMap? activePdoMap = null;
                    // Print the slave's actual PDO mapping so ErobPdoMap.Default can be
                    // updated to match the factory layout. This is SDO-only and should also
                    // run when PDO is disabled, because SAFE-OP may fail before PDO mode can log it.
                    try
                    {
                        await ErobPdoConfigurator.LogActiveMappingAsync(master, slaveIndex, AppendLog, cts.Token);
                        if (ethercatProcessDataEnabled)
                            activePdoMap = await ErobPdoConfigurator.ReadActiveMapAsync(master, slaveIndex, AppendLog, cts.Token);
                    }
                    catch (Exception mapEx)
                    {
                        AppendLog($"[eRob-PDO] Mapping discovery failed: {mapEx.Message}");
                    }

                    var adapter = new EthercatCoeCia402Adapter(master, slaveIndex, activePdoMap);
                    if (!adapter.HasTargetPosition || !adapter.HasTargetVelocity || !adapter.HasTargetTorque
                        || !adapter.HasControlword || !adapter.HasOperationMode)
                    {
                        AppendLog(
                            $"[eRob-PDO] RPDO is missing cyclic field(s): " +
                            $"TargetPosition(0x607A)={adapter.HasTargetPosition}, " +
                            $"TargetVelocity(0x60FF)={adapter.HasTargetVelocity}, " +
                            $"TargetTorque(0x6071)={adapter.HasTargetTorque}, " +
                            $"Controlword(0x6040)={adapter.HasControlword}, " +
                            $"Mode(0x6060)={adapter.HasOperationMode}.");
                    }

                    if (!adapter.HasVelocityActualValue || !adapter.HasTorqueActualValue || !adapter.HasErrorCode)
                    {
                        AppendLog(
                            $"[eRob-PDO] TxPDO is missing live field(s): " +
                            $"Velocity(0x606C)={adapter.HasVelocityActualValue}, " +
                            $"Torque(0x6077)={adapter.HasTorqueActualValue}, " +
                            $"ErrorCode(0x603F)={adapter.HasErrorCode}. Missing fields will be read by SDO.");
                    }

                    bool cia402ProcessDataActive = Cia402EnablePdo && ethercatProcessDataEnabled;
                    IDriveController controller = DriveControllerFactory.CreateCia402Controller(
                        adapter,
                        cia402ProcessDataActive ? adapter : null);

                    _soemMaster = master;
                    _cia402Controller = controller;
                    _cia402ProcessDataCapabilities = cia402ProcessDataActive ? adapter : null;

                    StartCia402PollLoop();
                    Cia402ProcessDataActive = cia402ProcessDataActive;
                    Cia402IsConnected = true;
                    master = null;
                }
                finally
                {
                    _ethercatLifecycleLock.Release();
                }
            }
            catch (Exception ex)
            {
                _cia402Controller = null;
                _soemMaster = null;
                _cia402ProcessDataCapabilities = null;
                Cia402ProcessDataActive = false;
                Cia402IsConnected = false;
                if (master != null)
                    await SafeDisposeEthercatMasterAsync(master);
                AppendLog($"[EtherCAT] Connect error: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

        private async Task DisconnectEthercatAsync()
        {
            await _ethercatLifecycleLock.WaitAsync();
            try
            {
                StopCia402PollLoop();
                _cia402Controller = null;
                _cia402ProcessDataCapabilities = null;
                Cia402ProcessDataActive = false;
                var master = _soemMaster;

                if (master == null)
                    return;

                try
                {
                    await master.CloseAsync();
                    AppendLog("[EtherCAT] Closed — slaves returned to Init state.");
                }
                catch (Exception ex)
                {
                    AppendLog($"[EtherCAT] Disconnect error: {ex.Message}");
                    LogHelper.Exception(ex);
                }
                finally
                {
                    if (ReferenceEquals(_soemMaster, master))
                        _soemMaster = null;

                    try
                    {
                        master.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }
            }
            finally
            {
                _ethercatLifecycleLock.Release();
            }
        }

        private static async Task SafeDisposeEthercatMasterAsync(SoemMaster master)
        {
            try
            {
                await master.CloseAsync();
            }
            catch
            {
                // Best-effort cleanup after a failed connect.
            }
            finally
            {
                master.Dispose();
            }
        }

        private bool TryValidateCia402NodeId(out byte nodeId)
        {
            nodeId = Config.Cia402NodeId;
            if (nodeId is >= 1 and <= 127)
                return true;

            AppendLog($"[CiA402] Invalid Node ID '{nodeId}'. CANopen node IDs must be in range 1..127.");
            return false;
        }

        // ── CiA 402 poll loop ──────────────────────────────────────────────────
        private bool TryValidateEthercatSlaveIndex(out ushort slaveIndex)
        {
            slaveIndex = Config.EthercatSlaveIndex;
            if (slaveIndex >= 1)
                return true;

            AppendLog($"[EtherCAT] Invalid Slave Index '{slaveIndex}'. EtherCAT slave indices must be >= 1.");
            return false;
        }

        private bool TryResolveEthercatInterface(out string interfaceName)
        {
            IReadOnlyList<SoemNetworkAdapter> presentAdapters = RefreshEthercatAdapters();

            SoemNetworkAdapter? matchedAdapter = SelectedEthercatAdapter;
            if (matchedAdapter == null)
            {
                AppendLog("[EtherCAT] No valid adapter is selected. Scan adapters and select one before connecting.");
                interfaceName = string.Empty;
                return false;
            }

            bool stillPresent = presentAdapters.Any(a =>
                string.Equals(a.Name, matchedAdapter.Name, StringComparison.OrdinalIgnoreCase));

            if (!stillPresent)
            {
                AppendLog($"[EtherCAT] Saved adapter '{matchedAdapter.Name}' is no longer present — pick a new one.");
                _selectedEthercatAdapter = null;
                EthercatInterface = string.Empty;
                RaisePropertyChanged(nameof(SelectedEthercatAdapter));
                interfaceName = string.Empty;
                return false;
            }

            interfaceName = matchedAdapter.Name;
            EthercatInterface = interfaceName;
            return true;
        }

        private static string NormalizeEthercatInterfaceName(string? value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            while (normalized.Contains("\\\\", StringComparison.Ordinal))
                normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);
            return normalized;
        }

        private void StartCia402PollLoop()
        {
            StopCia402PollLoop();
            _cia402PollCts = new CancellationTokenSource();
            Task.Run(() => Cia402PollLoopAsync(_cia402PollCts.Token));
        }

        private void StopCia402PollLoop()
        {
            _cia402PollCts?.Cancel();
            _cia402PollCts = null;

            void ApplyUiReset()
            {
                Cia402IsConnected = false;
                Cia402ProcessDataActive = false;
                _cia402ProcessDataCapabilities = null;
                StopCia402CyclicStreaming(log: false);
                ResetCia402StatusIndicators();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                ApplyUiReset();
            else
                dispatcher.Invoke(ApplyUiReset);
        }

        private async Task Cia402PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // If the SOEM cyclic thread died, surface the fault and stop polling.
                    var cyclicFault = _soemMaster?.CyclicFault;
                    if (cyclicFault != null)
                    {
                        AppendLog($"[EtherCAT] Cyclic thread fault — disconnecting: {cyclicFault.Message}");
                        LogHelper.Exception(cyclicFault);
                        _ = DisconnectEthercatAsync();
                        break;
                    }

                    var controleler = _cia402Controller;
                    if (controleler != null)
                    {
                        DriveSnapshot snapshot;
                        await _cia402IoLock.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            if (Cia402CyclicStreamingActive)
                            {
                                if (!Cia402ProcessDataActive)
                                {
                                    Application.Current?.Dispatcher.Invoke(() => StopCia402CyclicStreaming());
                                }
                                else
                                {
                                    await PushCia402CyclicTargetAsync(controleler, ct);
                                }
                            }

                            snapshot = await controleler.ReadSnapshotAsync(ct);
                        }
                        finally
                        {
                            _cia402IoLock.Release();
                        }

                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher == null)
                            continue;

                        dispatcher.Invoke(() =>
                        {
                            ApplyCia402Statusword(snapshot.Statusword);
                            Cia402StatusText = snapshot.StatusText;
                            Cia402Position   = Math.Round(Cia402CountsToDegrees(snapshot.Position), 4);
                            Cia402Velocity   = Math.Round(Cia402CountsPerSecondToRpm(snapshot.Velocity), 4);
                            Cia402Torque     = Math.Round(snapshot.Torque,   4);
                            Cia402ErrorCode  = snapshot.ErrorCode;
                        });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    AppendLog($"[CiA402] Poll error: {ex.Message}");
                    LogHelper.Exception(ex);
                }

                await Task.Delay(GetCia402LoopIntervalMs(), ct).ConfigureAwait(false);
            }
        }

        private int GetCia402LoopIntervalMs()
        {
            return Cia402CyclicStreamingActive
                ? Math.Max(Cia402MinimumPdoCycleMs, Cia402PdoCycleMs)
                : Cia402DefaultPollIntervalMs;
        }

        private async Task PushCia402CyclicTargetAsync(IDriveController controller, CancellationToken ct)
        {
            if (_cia402CyclicStreamingMode is not { } mode)
                return;

            if (!IsCia402CyclicTargetMapped(mode, out string requiredObject))
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AppendLog($"[CiA402] Cyclic stream stopped because {requiredObject} is not mapped in this drive's RPDO.");
                    StopCia402CyclicStreaming();
                });
                return;
            }

            double dtSeconds = Math.Max(Cia402MinimumPdoCycleMs, Cia402PdoCycleMs) / 1000.0;
            DriveCommand? command = null;
            switch (mode)
            {
                case Cia402OperationMode.CyclicSynchronousPosition:
                {
                    int targetCounts = DegreesToCia402Counts(Cia402TargetPosition);
                    int maxVelocityCountsPerSecond = Math.Max(1, Cia402ProfileVelocity);
                    int maxAccelerationCountsPerSecond2 = Math.Max(1, Cia402ProfileAcceleration);
                    _cia402CyclicPositionCommandCounts = SlewPositionTrapezoid(
                        _cia402CyclicPositionCommandCounts,
                        ref _cia402CyclicPositionVelocityCountsPerSecond,
                        targetCounts,
                        maxVelocityCountsPerSecond,
                        maxAccelerationCountsPerSecond2,
                        dtSeconds);

                    command = new WriteCyclicProcessDataDriveCommand(
                        TargetPosition: (int)Math.Clamp(
                            Math.Round(_cia402CyclicPositionCommandCounts),
                            int.MinValue,
                            int.MaxValue));
                    break;
                }
                case Cia402OperationMode.CyclicSynchronousVelocity:
                {
                    int targetVelocityCountsPerSecond = RpmToCia402CountsPerSecond(Cia402TargetVelocity);
                    int maxAccelerationCountsPerSecond2 = Math.Max(1, Cia402ProfileAcceleration);
                    _cia402CyclicVelocityCommandCountsPerSecond = SlewInt32(
                        _cia402CyclicVelocityCommandCountsPerSecond,
                        targetVelocityCountsPerSecond,
                        maxAccelerationCountsPerSecond2 * dtSeconds);

                    command = new WriteCyclicProcessDataDriveCommand(
                        TargetVelocity: _cia402CyclicVelocityCommandCountsPerSecond);
                    break;
                }
                case Cia402OperationMode.CyclicSynchronousTorque:
                {
                    if (!TryGetCia402TargetTorque(logOnError: false, out short targetTorque))
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            AppendLog($"[CiA402] Cyclic torque stream stopped because target {Cia402TargetTorque} is outside Int16 range.");
                            StopCia402CyclicStreaming();
                        });
                        return;
                    }

                    int torqueSlewPerSecond = Math.Max(
                        50,
                        Math.Max(Math.Abs((int)targetTorque), Math.Abs((int)_cia402CyclicTorqueCommand)));
                    _cia402CyclicTorqueCommand = (short)SlewInt32(
                        _cia402CyclicTorqueCommand,
                        targetTorque,
                        torqueSlewPerSecond * dtSeconds);

                    command = new WriteCyclicProcessDataDriveCommand(
                        TargetTorque: _cia402CyclicTorqueCommand);
                    break;
                }
            }

            if (command is null)
                return;

            try
            {
                await controller.ExecuteAsync(command, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not mapped", StringComparison.OrdinalIgnoreCase))
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AppendLog($"[CiA402] Cyclic stream stopped: {ex.Message}");
                    StopCia402CyclicStreaming();
                });
            }
        }

        private bool IsCia402CyclicTargetMapped(Cia402OperationMode mode, out string requiredObject)
        {
            requiredObject = mode switch
            {
                Cia402OperationMode.CyclicSynchronousPosition => "Target Position 0x607A",
                Cia402OperationMode.CyclicSynchronousVelocity => "Target Velocity 0x60FF",
                Cia402OperationMode.CyclicSynchronousTorque => "Target Torque 0x6071",
                _ => "cyclic target"
            };

            if (_cia402ProcessDataCapabilities is not { } caps)
                return true;

            return mode switch
            {
                Cia402OperationMode.CyclicSynchronousPosition => caps.HasTargetPosition,
                Cia402OperationMode.CyclicSynchronousVelocity => caps.HasTargetVelocity,
                Cia402OperationMode.CyclicSynchronousTorque => caps.HasTargetTorque,
                _ => true
            };
        }

        private static double SlewPositionTrapezoid(
            double current,
            ref double currentVelocity,
            double target,
            double maxVelocity,
            double maxAcceleration,
            double dt)
        {
            if (dt <= 0)
                return current;

            double error = target - current;
            if (Math.Abs(error) < 0.5 && Math.Abs(currentVelocity) < 0.5)
            {
                currentVelocity = 0;
                return target;
            }

            double direction = Math.Sign(error);
            double stoppingDistance = currentVelocity * currentVelocity / (2.0 * Math.Max(1.0, maxAcceleration));
            double desiredVelocity = Math.Abs(error) <= stoppingDistance
                ? 0
                : direction * Math.Max(0, maxVelocity);

            currentVelocity = SlewDouble(currentVelocity, desiredVelocity, maxAcceleration * dt);
            currentVelocity = Math.Clamp(currentVelocity, -maxVelocity, maxVelocity);

            double next = current + currentVelocity * dt;
            if (Math.Sign(target - next) != direction)
            {
                currentVelocity = 0;
                return target;
            }

            return next;
        }

        private static double SlewDouble(double current, double target, double maxStep)
        {
            if (Math.Abs(target - current) <= maxStep)
                return target;

            return current + Math.Sign(target - current) * maxStep;
        }

        private static int SlewInt32(int current, int target, double maxStep)
        {
            if (current == target)
                return target;

            int step = Math.Max(1, (int)Math.Round(maxStep));
            long delta = (long)target - current;
            if (Math.Abs(delta) <= step)
                return target;

            long next = current + Math.Sign(delta) * (long)step;
            return (int)Math.Clamp(next, int.MinValue, int.MaxValue);
        }

        private void ApplyCia402Statusword(ushort rawStatusword)
        {
            Cia402Statusword sw = new(rawStatusword);
            Cia402StatuswordRaw = rawStatusword;
            Cia402ReadyToSwitchOn = sw.ReadyToSwitchOn;
            Cia402SwitchedOn = sw.SwitchedOn;
            Cia402OperationEnabled = sw.OperationEnabled;
            Cia402Fault = sw.Fault;
            Cia402VoltageEnabled = sw.VoltageEnabled;
            Cia402QuickStopNotActive = sw.QuickStopNotActive;
            Cia402SwitchOnDisabled = sw.SwitchOnDisabled;
            Cia402Warning = sw.Warning;
            Cia402Remote = sw.Remote;
            Cia402TargetReached = sw.TargetReached;
            Cia402InternalLimitActive = sw.InternalLimitActive;
            Cia402SetPointAcknowledge = sw.SetPointAcknowledge;
            Cia402FollowingError = sw.FollowingError;
        }

        private void ResetCia402StatusIndicators()
        {
            Cia402StatusText = "—";
            Cia402StatuswordRaw = 0;
            Cia402ReadyToSwitchOn = false;
            Cia402SwitchedOn = false;
            Cia402OperationEnabled = false;
            Cia402Fault = false;
            Cia402VoltageEnabled = false;
            Cia402QuickStopNotActive = false;
            Cia402SwitchOnDisabled = false;
            Cia402Warning = false;
            Cia402Remote = false;
            Cia402TargetReached = false;
            Cia402InternalLimitActive = false;
            Cia402SetPointAcknowledge = false;
            Cia402FollowingError = false;
        }

        // ── CiA 402 command execution ──────────────────────────────────────────
        private void ExecuteCia402StateMachineCommand(DriveCommand command)
        {
            StopCia402CyclicStreaming(log: false);
            ExecuteCia402Command(command);
        }

        private void ExecuteCia402PositionCommand()
        {
            switch (SelectedCia402Mode)
            {
                case Cia402OperationMode.ProfilePosition:
                {
                    StopCia402CyclicStreaming(log: false);
                    int timeoutMs = Math.Max(1, Cia402ProfileAckTimeoutMs);
                    ExecuteCia402Command(new MoveAbsolutePositionDriveCommand(
                        DegreesToCia402Counts(Cia402TargetPosition),
                        Cia402PositionCommandMode.ProfilePosition,
                        Cia402ProfileImmediateChange,
                        TimeSpan.FromMilliseconds(timeoutMs),
                        ProfileVelocity: Cia402ProfileVelocity,
                        ProfileAcceleration: Cia402ProfileAcceleration,
                        ProfileDeceleration: Cia402ProfileDeceleration));
                    return;
                }
                case Cia402OperationMode.CyclicSynchronousPosition:
                    if (!EnsureCia402PdoReadyForCyclic("CSP move", Cia402OperationMode.CyclicSynchronousPosition))
                        return;

                    StartCia402CyclicStreaming(
                        Cia402OperationMode.CyclicSynchronousPosition,
                        $"Target Position={Cia402TargetPosition} deg");
                    return;
                default:
                    AppendLog($"[CiA402] Move requires {Cia402OperationMode.ProfilePosition} or {Cia402OperationMode.CyclicSynchronousPosition}. Current mode is {SelectedCia402Mode}.");
                    return;
            }
        }

        private void ExecuteCia402VelocityCommand()
        {
            if (!Cia402OperationEnabled)
            {
                AppendLog("[CiA402] Velocity command ignored because the drive is not Operation Enabled. Run Shutdown -> Switch On -> Enable Operation first.");
                return;
            }

            switch (SelectedCia402Mode)
            {
                case Cia402OperationMode.ProfileVelocity:
                    StopCia402CyclicStreaming(log: false);
                    ExecuteCia402Command(new SetVelocityDriveCommand(RpmToCia402CountsPerSecond(Cia402TargetVelocity)));
                    return;
                case Cia402OperationMode.CyclicSynchronousVelocity:
                    if (!EnsureCia402PdoReadyForCyclic("CSV setpoint", Cia402OperationMode.CyclicSynchronousVelocity))
                        return;

                    StartCia402CyclicStreaming(
                        Cia402OperationMode.CyclicSynchronousVelocity,
                        $"Target Velocity={Cia402TargetVelocity} rpm");
                    return;
                default:
                    AppendLog($"[CiA402] Velocity command requires {Cia402OperationMode.ProfileVelocity} or {Cia402OperationMode.CyclicSynchronousVelocity}. Current mode is {SelectedCia402Mode}.");
                    return;
            }
        }

        private void ExecuteCia402TorqueCommand()
        {
            if (!TryGetCia402TargetTorque(logOnError: true, out short targetTorque))
                return;

            switch (SelectedCia402Mode)
            {
                case Cia402OperationMode.ProfileTorque:
                    StopCia402CyclicStreaming(log: false);
                    ExecuteCia402Command(new SetTorqueDriveCommand(targetTorque));
                    return;
                case Cia402OperationMode.CyclicSynchronousTorque:
                    if (!EnsureCia402PdoReadyForCyclic("CST setpoint", Cia402OperationMode.CyclicSynchronousTorque))
                        return;

                    StartCia402CyclicStreaming(
                        Cia402OperationMode.CyclicSynchronousTorque,
                        $"Target Torque={targetTorque}");
                    return;
                default:
                    AppendLog($"[CiA402] Torque command requires {Cia402OperationMode.ProfileTorque} or {Cia402OperationMode.CyclicSynchronousTorque}. Current mode is {SelectedCia402Mode}.");
                    return;
            }
        }

        private bool EnsureCia402PdoReadyForCyclic(string actionLabel, Cia402OperationMode cyclicMode)
        {
            if (_cia402Controller == null || !Cia402IsConnected)
            {
                AppendLog($"[CiA402] {actionLabel} requires an active CiA402 connection.");
                return false;
            }

            if (!Cia402EnablePdo)
            {
                AppendLog($"[CiA402] {actionLabel} requires Enable PDO.");
                return false;
            }

            if (!Cia402ProcessDataActive)
            {
                AppendLog($"[CiA402] {actionLabel} requires active PDO process data. Disconnect and reconnect with Enable PDO on.");
                return false;
            }

            if (_cia402ProcessDataCapabilities is { } caps)
            {
                bool mapped = cyclicMode switch
                {
                    Cia402OperationMode.CyclicSynchronousPosition => caps.HasTargetPosition,
                    Cia402OperationMode.CyclicSynchronousVelocity => caps.HasTargetVelocity,
                    Cia402OperationMode.CyclicSynchronousTorque => caps.HasTargetTorque,
                    _ => true
                };

                if (!mapped)
                {
                    string requiredObject = cyclicMode switch
                    {
                        Cia402OperationMode.CyclicSynchronousPosition => "Target Position 0x607A",
                        Cia402OperationMode.CyclicSynchronousVelocity => "Target Velocity 0x60FF",
                        Cia402OperationMode.CyclicSynchronousTorque => "Target Torque 0x6071",
                        _ => "cyclic target"
                    };

                    AppendLog($"[CiA402] {actionLabel} requires {requiredObject} mapped in RPDO. Current active PDO map does not expose it.");
                    return false;
                }
            }

            return true;
        }

        private bool TryGetCia402TargetTorque(bool logOnError, out short targetTorque)
        {
            if (Cia402TargetTorque is < short.MinValue or > short.MaxValue)
            {
                targetTorque = 0;
                if (logOnError)
                    AppendLog($"[CiA402] Target torque {Cia402TargetTorque} is out of Int16 range ({short.MinValue}..{short.MaxValue}).");

                return false;
            }

            targetTorque = (short)Cia402TargetTorque;
            return true;
        }

        private bool TryGetPositiveCia402TargetTorque(string actionLabel, out short targetTorque)
        {
            if (!TryGetCia402TargetTorque(logOnError: true, out targetTorque))
                return false;

            if (targetTorque > 0)
                return true;

            AppendLog($"[CiA402] {actionLabel} requires Target Torque > 0. eRob velocity mode uses Target Torque as the max torque/current limit.");
            return false;
        }

        private int DegreesToCia402Counts(double degrees)
        {
            double counts = degrees / 360.0 * Cia402CountsPerRevolution;
            return (int)Math.Clamp(Math.Round(counts), int.MinValue, int.MaxValue);
        }

        private int RpmToCia402CountsPerSecond(double rpm)
        {
            double countsPerSecond = rpm * Cia402CountsPerRevolution / 60.0;
            return (int)Math.Clamp(Math.Round(countsPerSecond), int.MinValue, int.MaxValue);
        }

        private double Cia402CountsToDegrees(double counts)
            => counts * 360.0 / Cia402CountsPerRevolution;

        private double Cia402CountsPerSecondToRpm(double countsPerSecond)
            => countsPerSecond * 60.0 / Cia402CountsPerRevolution;

        private void StartCia402CyclicStreaming(Cia402OperationMode mode, string description)
        {
            bool wasActive = Cia402CyclicStreamingActive;
            bool sameMode = _cia402CyclicStreamingMode == mode;

            if (!wasActive || !sameMode)
                ResetCia402CyclicCommandState(mode);

            _cia402CyclicStreamingMode = mode;
            Cia402CyclicStreamingDescription = description;
            Cia402CyclicStreamingActive = true;

            if (wasActive && sameMode)
            {
                AppendLog($"[CiA402] Updated cyclic PDO target: {description}");
            }
            else
            {
                AppendLog($"[CiA402] Started cyclic PDO stream: {mode} ({description}). Targets are ramped from the current feedback value.");
            }
        }

        private void ResetCia402CyclicCommandState(Cia402OperationMode mode)
        {
            switch (mode)
            {
                case Cia402OperationMode.CyclicSynchronousPosition:
                    _cia402CyclicPositionCommandCounts = DegreesToCia402Counts(Cia402Position);
                    _cia402CyclicPositionVelocityCountsPerSecond = 0;
                    break;
                case Cia402OperationMode.CyclicSynchronousVelocity:
                    _cia402CyclicVelocityCommandCountsPerSecond = RpmToCia402CountsPerSecond(Cia402Velocity);
                    break;
                case Cia402OperationMode.CyclicSynchronousTorque:
                    _cia402CyclicTorqueCommand = (short)Math.Clamp(
                        (int)Math.Round(Cia402Torque),
                        short.MinValue,
                        short.MaxValue);
                    break;
            }
        }

        private void StopCia402CyclicStreaming(bool log = true)
        {
            bool wasActive = Cia402CyclicStreamingActive;
            string description = Cia402CyclicStreamingDescription;

            Cia402CyclicStreamingActive = false;
            _cia402CyclicStreamingMode = null;
            Cia402CyclicStreamingDescription = "Idle";
            _cia402CyclicPositionVelocityCountsPerSecond = 0;

            if (log && wasActive)
                AppendLog($"[CiA402] Cyclic PDO stream stopped ({description}).");
        }

        private void ExecuteCia402Command(DriveCommand command)
        {
            var controller = _cia402Controller;
            if (controller == null)
            {
                AppendLog("[CiA402] Not connected — command ignored.");
                return;
            }
            _ = ExecuteCia402CommandAsync(controller, command);
        }

        private async Task ExecuteCia402CommandAsync(IDriveController controller, DriveCommand command)
        {
            string name = command.GetType().Name.Replace("DriveCommand", string.Empty);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _cia402IoLock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    await controller.ExecuteAsync(command, cts.Token);
                }
                finally
                {
                    _cia402IoLock.Release();
                }
                AppendLog($"[CiA402] {name} OK");
            }
            catch (TimeoutException ex)
            {
                AppendLog($"[CiA402] {name} TIMEOUT: {ex.Message}");
                LogHelper.Exception(ex);
            }
            catch (Exception ex)
            {
                AppendLog($"[CiA402] {name} ERROR: {ex.Message}");
                LogHelper.Exception(ex);
            }
        }

    }
} 
