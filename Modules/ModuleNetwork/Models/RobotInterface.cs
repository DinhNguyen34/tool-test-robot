using System;
using System.Net;
using System.Net.Sockets;

namespace ModuleNetwork.Models
{
    /// <summary>
    /// C# port of RobotInterface from nuc_server.py.
    /// Manages two UDP sockets:
    ///   RX — listens for LowState packets from STM32.
    ///   TX — sends LowCmd packets to STM32 at <see cref="SendRateHz"/> Hz.
    /// Thread-safe: all shared state is guarded by <see cref="_lock"/>.
    /// </summary>
    public class RobotInterface : IDisposable
    {
        // ── Network defaults (mirrors nuc_server.py constants) ────────────────
        public const string StmIpLeg      = "192.168.1.10";
        public const string StmIpArm      = "192.168.1.6";
        public const int    StmPortLeg    = 8889;
        public const int    StmPortArm    = 8888;
        public const int    ListenPortLeg = 12345;
        public const int    ListenPortArm = 12346;
        public const int    SendRateHz    = 500;

        // ── Identity ──────────────────────────────────────────────────────────
        public RobotMode Mode       { get; }
        public int       NMotors    { get; }
        public int       ListenPort { get; }
        public string    StmIp      { get; }
        public int       StmPort    { get; }

        // ── Statistics ────────────────────────────────────────────────────────
        public int  RxCount   { get; private set; }
        public int  TxCount   { get; private set; }
        public int  CrcErrors { get; private set; }
        public bool IsRunning { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Raised on the RX background thread when a valid CRC state arrives.</summary>
        public event Action<LowState>? OnStateReceived;
        /// <summary>Raised for info/error messages (use Dispatcher.Invoke to update UI).</summary>
        public event Action<string>?   OnLog;

        // ── Shared state ──────────────────────────────────────────────────────
        private readonly object _lock = new();
        private LowState _state;
        private LowCmd   _cmd;

        // ── Sockets & tasks ───────────────────────────────────────────────────
        private UdpClient?               _rxSocket;
        private UdpClient?               _txSocket;
        private CancellationTokenSource? _cts;
        private Task?                    _rxTask;
        private Task?                    _txTask;

        // ── Constructor ───────────────────────────────────────────────────────
        public RobotInterface(RobotMode mode = RobotMode.Leg)
        {
            Mode       = mode;
            NMotors    = mode == RobotMode.Leg ? 12 : 20;
            ListenPort = mode == RobotMode.Leg ? ListenPortLeg : ListenPortArm;
            StmIp      = mode == RobotMode.Leg ? StmIpLeg      : StmIpArm;
            StmPort    = mode == RobotMode.Leg ? StmPortLeg     : StmPortArm;

            _state = new LowState { Motors = Enumerable.Range(0, NMotors).Select(_ => new MotorState()).ToList() };
            _cmd   = new LowCmd   { Motors = Enumerable.Range(0, NMotors).Select(_ => new MotorCmd()).ToList() };
        }

        // ── Start / Stop ──────────────────────────────────────────────────────
        public void Start()
        {
            if (IsRunning) return;

            // RX socket: bind to listen port
            _rxSocket = new UdpClient();
            _rxSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _rxSocket.Client.Bind(new IPEndPoint(IPAddress.Any, ListenPort));

            // TX socket: connected to STM32
            _txSocket = new UdpClient();
            _txSocket.Connect(StmIp, StmPort);

            _cts      = new CancellationTokenSource();
            IsRunning = true;

            _rxTask = Task.Run(() => RxLoopAsync(_cts.Token));
            _txTask = Task.Run(() => TxLoopAsync(_cts.Token));

            Log($"RobotInterface started [{Mode}] — listen:{ListenPort}  →  {StmIp}:{StmPort}");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            // send zero command before closing
            try
            {
                var zero = MakeZeroCmd();
                _txSocket?.Send(NucProtocol.BuildLowCmd(zero, NMotors));
            }
            catch { }

            _cts?.Cancel();
            _rxTask?.Wait(2000);
            _txTask?.Wait(2000);

            _rxSocket?.Close();
            _txSocket?.Close();
            _rxSocket = null;
            _txSocket = null;

            Log("RobotInterface stopped.");
        }

        // ── State / Cmd accessors (thread-safe) ───────────────────────────────
        public LowState GetState() { lock (_lock) { return _state; } }

        public void SetCmd(LowCmd cmd) { lock (_lock) { _cmd = cmd; } }

        public void SetMotorCmd(int index, float q = 0, float dq = 0,
                                           float tau = 0, float kp = 0, float kd = 0)
        {
            lock (_lock)
            {
                if (index >= 0 && index < NMotors)
                    _cmd.Motors[index] = new MotorCmd { Q = q, Dq = dq, Tau = tau, Kp = kp, Kd = kd };
                else
                    Log($"SetMotorCmd: index {index} out of range (0..{NMotors - 1})");
            }
        }

        // ── Convenience helpers ───────────────────────────────────────────────
        public List<float> GetMotorAngles()
            => GetState().Motors.Select(m => m.Q).ToList();

        public List<int> GetMotorErrors()
            => GetState().Motors.Select(m => m.ErrorCode).ToList();

        public bool HasErrors()
            => GetState().Motors.Any(m => m.ErrorCode != 0);

        // ── Internal RX loop ──────────────────────────────────────────────────
        private async Task RxLoopAsync(CancellationToken ct)
        {
            Log("RX thread started");
            while (!ct.IsCancellationRequested && _rxSocket != null)
            {
                try
                {
                    var result = await _rxSocket.ReceiveAsync(ct);
                    var state  = NucProtocol.ParseLowState(result.Buffer, NMotors);
                    if (state == null) continue;

                    RxCount++;
                    if (!state.CrcOk) CrcErrors++;

                    lock (_lock) { _state = state; }

                    if (state.CrcOk)
                        OnStateReceived?.Invoke(state);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (IsRunning) Log($"RX error: {ex.Message}");
                }
            }
            Log("RX thread stopped");
        }

        // ── Internal TX loop ──────────────────────────────────────────────────
        private async Task TxLoopAsync(CancellationToken ct)
        {
            Log("TX thread started");
            int periodMs = 1000 / SendRateHz; // = 2 ms at 500 Hz

            while (!ct.IsCancellationRequested && _txSocket != null)
            {
                var t0 = DateTime.UtcNow;

                LowCmd copy;
                lock (_lock) { copy = new LowCmd { Motors = new List<MotorCmd>(_cmd.Motors) }; }

                try
                {
                    var raw = NucProtocol.BuildLowCmd(copy, NMotors);
                    await _txSocket.SendAsync(raw, raw.Length);
                    TxCount++;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (IsRunning) Log($"TX error: {ex.Message}");
                }

                var elapsed = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                var sleep   = periodMs - elapsed;
                if (sleep > 0)
                    await Task.Delay(sleep, ct).ConfigureAwait(false);
            }

            Log("TX thread stopped");
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private LowCmd MakeZeroCmd()
            => new() { Motors = Enumerable.Range(0, NMotors).Select(_ => new MotorCmd()).ToList() };

        private void Log(string msg) => OnLog?.Invoke(msg);

        public void Dispose() => Stop();
    }
}
