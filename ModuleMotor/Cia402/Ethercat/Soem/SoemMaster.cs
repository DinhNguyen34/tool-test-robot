using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ModuleMotor.Cia402.Ethercat.Soem
{
    public sealed class SoemMaster : IDisposable
    {
        private const int IoMapSize = 4096;
        private const int SdoReadAttempts = 3;
        private static readonly TimeSpan SdoRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly byte[] _ioMap = new byte[IoMapSize];
        private readonly object _ioMapLock = new();
        private readonly object _nativeLock = new();
        private readonly string _interfaceName;
        private readonly int _cyclePeriodMs;

        private GCHandle _ioMapHandle;
        private IntPtr _ioMapPtr;
        private IntPtr _context;
        private IntPtr _scanInfoBuffer;
        private volatile bool _running;
        private Thread? _cyclicThread;
        private int _slaveCount;
        private int _totalIoBytes;
        private long _dcTime;
        private bool _disposed;
        private Exception? _cyclicFault;
        private int _dcSync0ConfiguredSlaves;

        public SoemMaster(string interfaceName, int cyclePeriodMs = 1)
        {
            _interfaceName = interfaceName;
            _cyclePeriodMs = Math.Max(1, cyclePeriodMs);
        }

        public int SlaveCount => _slaveCount;
        public int OutputRegionOffset { get; private set; }
        public int InputRegionOffset { get; private set; }
        public int OutputBytes { get; private set; }
        public int InputBytes { get; private set; }
        public long DcTime => Interlocked.Read(ref _dcTime);

        /// <summary>
        /// Non-null when the cyclic PDO thread has faulted and stopped.
        /// Poll this after <see cref="OpenAsync"/> to detect silent link-loss.
        /// </summary>
        public Exception? CyclicFault => Volatile.Read(ref _cyclicFault);

        public Task OpenAsync(CancellationToken ct = default)
            => OpenAsync(configurePdoMappingAsync: null, ct);

        /// <summary>
        /// Opens the EtherCAT bus. When process data is enabled, maps PDOs and brings
        /// every slave to OPERATIONAL; otherwise leaves slaves in PRE-OP for SDO-only access.
        /// </summary>
        /// <param name="configurePdoMappingAsync">
        /// Optional hook invoked while the slaves are in PRE-OP, after the bus has been
        /// scanned but before <c>ec_config_map_group</c> computes IO sizes. Use it to
        /// program PDO mapping objects (0x1600/0x1A00) and assignments (0x1C12/0x1C13)
        /// via <see cref="SdoWriteAsync"/> so the active layout matches what the managed
        /// adapter expects to read and write.
        /// </param>
        public async Task OpenAsync(
            Func<SoemMaster, CancellationToken, Task>? configurePdoMappingAsync,
            CancellationToken ct = default,
            bool enableProcessData = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureIoMapPinned();

            try
            {
                // Phase A: ec_init + ec_config_init → slaves in PRE-OP, mailbox active.
                await Task.Run(() =>
                {
                    if (!SoemNative.TryEnsureLoaded(out string loadError))
                        throw new InvalidOperationException(loadError);

                    _context = SoemNative.CreateContext();
                    if (_context == IntPtr.Zero)
                        throw new InvalidOperationException("SOEM bridge: CreateContext returned NULL.");

                    int scanResult = SoemNative.ScanDevices(
                        _context,
                        _interfaceName,
                        out _scanInfoBuffer,
                        out int slaveCount);

                    if (slaveCount <= 0 || _scanInfoBuffer == IntPtr.Zero)
                    {
                        IReadOnlyList<string> known = SoemNative.GetKnownAdapterNames();
                        string adapterHint = known.Count > 0
                            ? $" Known adapters: {string.Join(" | ", known)}."
                            : string.Empty;
                        string npcapHint = SoemNative.GetNpcapOpenFailureHint();

                        bool selectedAdapterMissing = scanResult == 0
                            && known.Count > 0
                            && !known.Any(name => string.Equals(name, _interfaceName, StringComparison.OrdinalIgnoreCase));
                        string missingPrefix = selectedAdapterMissing
                            ? "Selected adapter is not present on this machine. "
                            : string.Empty;

                        throw new InvalidOperationException(
                            $"{missingPrefix}SOEM bridge: ScanDevices failed for '{_interfaceName}' (code={scanResult}).{adapterHint} {npcapHint}");
                    }

                    _slaveCount = slaveCount;
                }, ct).ConfigureAwait(false);

                // Optional hook in PRE-OP: program PDO mapping via SDO before ec_config_map_group
                // finalizes IO sizes. Mailbox-based SDO writes work as soon as ec_config_init
                // completes.
                if (configurePdoMappingAsync != null)
                    await configurePdoMappingAsync(this, ct).ConfigureAwait(false);

                if (!enableProcessData)
                {
                    await PrepareMailboxOnlySessionAsync(ct).ConfigureAwait(false);
                    return;
                }

                // Phase B: ec_config_map_group (PRE-OP→SAFE-OP), DC sync, OPERATIONAL transition.
                await Task.Run(() =>
                {
                    int[] outputOffsets = new int[_slaveCount + 1];
                    int[] inputOffsets = new int[_slaveCount + 1];
                    int mapResult = SoemNative.ConfigureIoMap(
                        _context,
                        _ioMapPtr,
                        outputOffsets,
                        inputOffsets,
                        out int ioSegments);

                    if (mapResult <= 0)
                    {
                        throw new InvalidOperationException(
                            $"SOEM bridge: ConfigureIoMap failed (code={mapResult}, segments={ioSegments}).");
                    }

                    ApplyIoLayout(outputOffsets, inputOffsets, mapResult);
                    ConfigureDcSync0IfAvailable();

                    if (!RequestCommonStateWithAlErrorRecovery(SoemNative.EC_STATE_SAFE_OP))
                        throw new InvalidOperationException(
                            $"SOEM bridge: failed to reach SAFE-OP. {DescribeSlaveStates()}");

                    PumpProcessData(cycles: 50);

                    if (!RequestOperationalWithWatchdogRecovery())
                        throw new InvalidOperationException(
                            $"SOEM bridge: failed to reach OPERATIONAL. {DescribeCyclicWkc()} {DescribeSlaveStates()}");

                    PumpProcessData(cycles: 10);
                }, ct).ConfigureAwait(false);

                _running = true;
                _cyclicThread = new Thread(CyclicLoop)
                {
                    Name = "SOEM-PDO",
                    Priority = ThreadPriority.Highest,
                    IsBackground = true
                };
                _cyclicThread.Start();
            }
            catch
            {
                _running = false;
                try
                {
                    CloseNativeSession(requestInitState: false);
                }
                finally
                {
                    ReleaseIoMap();
                }

                throw;
            }
        }

        public async Task CloseAsync()
        {
            _running = false;

            Thread? t = _cyclicThread;
            if (t != null)
                await Task.Run(() => t.Join(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            _cyclicThread = null;

            await Task.Run(() => CloseNativeSession(requestInitState: true)).ConfigureAwait(false);
            ReleaseIoMap();
        }

        /// <summary>
        /// Broadcasts a PRE-OP state request and waits for every slave to settle there.
        /// Use before issuing SDO writes to PDO-mapping objects from the OpenAsync hook —
        /// <c>ec_config_init</c> sometimes returns before the slave's mailbox is ready,
        /// causing the first SDO write to fail with WKC=0.
        /// </summary>
        public Task EnsurePreOpAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                lock (_nativeLock)
                {
                    EnsureContextOpen();
                    int result = SoemNative.RequestCommonState(_context, SoemNative.EC_STATE_PRE_OP);
                    if (result <= 0)
                    {
                        throw new InvalidOperationException(
                            $"SOEM bridge: failed to settle slaves in PRE-OP for PDO mapping. {DescribeSlaveStates()}");
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Returns the AL status code (last EtherCAT-defined error reason) for one slave.
        /// Returns false on bridges that don't export GetAlStatusCode.
        /// </summary>
        public bool TryGetSlaveAlStatusCode(int slaveIndex, out ushort alStatus)
        {
            alStatus = 0;
            if (_context == IntPtr.Zero || slaveIndex < 1 || slaveIndex > _slaveCount)
                return false;
            return SoemNative.TryGetAlStatusCode(_context, slaveIndex, out alStatus);
        }

        private async Task PrepareMailboxOnlySessionAsync(CancellationToken ct)
        {
            await EnsurePreOpAsync(ct).ConfigureAwait(false);
            await AcknowledgeAlErrorsAsync(ct).ConfigureAwait(false);
            await Task.Delay(SdoRetryDelay, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears any pending AL errors so the slave's mailbox accepts SDO traffic.
        /// A slave that is sitting in <c>state | EC_STATE_ERROR</c> (e.g. PRE-OP+ERROR)
        /// reports the base state to <c>ec_statecheck</c> while silently dropping CoE
        /// requests, which surfaces as WKC=0 on the first SDO write. For each slave with
        /// a non-zero ALstatuscode we send <c>baseState | EC_STATE_ACK</c> followed by the
        /// base state alone (the standard ETG.1000 ack handshake), then re-settle the bus
        /// in PRE-OP so subsequent SDOs land cleanly.
        /// </summary>
        public Task AcknowledgeAlErrorsAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                lock (_nativeLock)
                {
                    EnsureContextOpen();
                    bool anyAcked = AcknowledgeAlErrorsCore();

                    if (anyAcked)
                    {
                        int result = SoemNative.RequestCommonState(_context, SoemNative.EC_STATE_PRE_OP);
                        if (result <= 0)
                        {
                            throw new InvalidOperationException(
                                $"SOEM bridge: failed to re-settle slaves in PRE-OP after AL error ack. {DescribeSlaveStates()}");
                        }
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Returns the most recently read EtherCAT state of one slave (1-based index).
        /// Pair with <see cref="ReadStateRefresh"/> to ensure freshness.
        /// </summary>
        public ushort GetSlaveState(int slaveIndex)
        {
            if (_context == IntPtr.Zero) return 0;
            return SoemNative.GetState(_context, slaveIndex);
        }

        /// <summary>Refreshes the cached per-slave state by polling the bus.</summary>
        public void ReadStateRefresh()
        {
            if (_context == IntPtr.Zero) return;
            lock (_nativeLock)
            {
                EnsureContextOpen();
                SoemNative.ReadState(_context);
            }
        }

        private bool RequestCommonStateWithAlErrorRecovery(ushort requestedState)
        {
            if (SoemNative.RequestCommonState(_context, requestedState) > 0)
                return true;

            bool anyAcked = AcknowledgeAlErrorsCore();
            if (!anyAcked)
                return false;

            return SoemNative.RequestCommonState(_context, requestedState) > 0;
        }

        private bool RequestOperationalWithWatchdogRecovery()
        {
            const int attempts = 3;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                PumpProcessData(cycles: 50);
                if (SoemNative.RequestCommonState(_context, SoemNative.EC_STATE_OPERATIONAL) > 0)
                    return true;

                bool anyAcked = AcknowledgeAlErrorsCore();
                if (attempt == attempts)
                    return false;

                if (anyAcked)
                {
                    SoemNative.RequestCommonState(_context, SoemNative.EC_STATE_SAFE_OP);
                    PumpProcessData(cycles: 50);
                }
            }

            return false;
        }

        private void PumpProcessData(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                UpdateIoOnce();
                Thread.Sleep(_cyclePeriodMs);
            }
        }

        private bool AcknowledgeAlErrorsCore()
        {
            SoemNative.ReadState(_context);

            bool anyAcked = false;
            for (int slave = 1; slave <= _slaveCount; slave++)
            {
                ushort current = SoemNative.GetState(_context, slave);
                bool hasAlStatus = SoemNative.TryGetAlStatusCode(_context, slave, out ushort alStatus);
                bool hasErrorBit = (current & SoemNative.EC_STATE_ERROR) != 0;

                if (!hasErrorBit && (!hasAlStatus || alStatus == 0))
                    continue;

                ushort baseState = (ushort)(current & 0x0F);
                if (baseState == 0)
                    baseState = SoemNative.EC_STATE_PRE_OP;

                ushort ackState = (ushort)(baseState | SoemNative.EC_STATE_ACK);
                SoemNative.RequestState(_context, slave, ackState);
                SoemNative.RequestState(_context, slave, baseState);
                anyAcked = true;
            }

            return anyAcked;
        }

        public Task<byte[]> SdoReadAsync(
            ushort slave,
            ushort index,
            byte subIndex,
            int maxBytes,
            CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                byte[] raw = new byte[8];
                IntPtr buffer = Marshal.AllocHGlobal(raw.Length);
                try
                {
                    int result = 0;
                    string? abortDetail = null;

                    for (int attempt = 1; attempt <= SdoReadAttempts; attempt++)
                    {
                        lock (_nativeLock)
                        {
                            EnsureContextOpen();

                            // Discard any stale elist entries left by earlier calls so the
                            // post-call drain only reflects errors produced by *this* SDO.
                            PopSdoErrorDetail();

                            result = SoemNative.NoCaSdoRead(_context, slave, index, subIndex, buffer);

                            if (result <= 0)
                                abortDetail = PopSdoErrorDetail();
                        }

                        if (result > 0)
                        {
                            Marshal.Copy(buffer, raw, 0, raw.Length);
                            return maxBytes > 0 && maxBytes < raw.Length
                                ? raw.AsSpan(0, maxBytes).ToArray()
                                : raw;
                        }

                        if (attempt < SdoReadAttempts)
                            Thread.Sleep(SdoRetryDelay);
                    }

                    string abortSuffix = abortDetail != null ? $" {abortDetail}" : string.Empty;
                    throw new InvalidOperationException(
                        $"SOEM bridge SDO read failed after {SdoReadAttempts} attempts: " +
                        $"slave={slave} idx=0x{index:X4}:{subIndex:X2} code={result}.{abortSuffix} {DescribeSlaveStates()}");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }, ct);
        }

        public Task SdoWriteAsync(
            ushort slave,
            ushort index,
            byte subIndex,
            byte[] data,
            CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                IntPtr buffer = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);

                    int result;
                    string? abortDetail = null;
                    lock (_nativeLock)
                    {
                        EnsureContextOpen();

                        // Discard any stale elist entries left by earlier calls so the
                        // post-call drain only reflects errors produced by *this* SDO.
                        PopSdoErrorDetail();

                        result = SoemNative.EcxSdoWrite(
                            _context,
                            slave,
                            index,
                            subIndex,
                            false,
                            data.Length,
                            buffer,
                            SoemNative.EC_TIMEOUTRXM);

                        // Even on WKC>0 the slave may have aborted via CoE — drain so callers
                        // that read-back can also tell when the "successful" write was actually
                        // rejected.
                        abortDetail = PopSdoErrorDetail();
                    }

                    if (result <= 0)
                    {
                        string abortSuffix = abortDetail != null ? $" {abortDetail}" : string.Empty;
                        throw new InvalidOperationException(
                            $"SOEM bridge SDO write failed: slave={slave} idx=0x{index:X4}:{subIndex:X2} code={result}.{abortSuffix}");
                    }

                    if (abortDetail != null)
                    {
                        // WKC said the mailbox transaction completed, but the slave responded
                        // with an abort — the write did NOT take effect. Treat as a failure
                        // so write-then-readback paths surface the real reason instead of
                        // hitting the opaque WKC=0 readback that follows.
                        throw new InvalidOperationException(
                            $"SOEM bridge SDO write rejected by slave: slave={slave} idx=0x{index:X4}:{subIndex:X2} wkc={result}. {abortDetail}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }, ct);
        }

        // Caller must hold _nativeLock. Returns a formatted "abort=0x… (reason) on idx=…"
        // string when an error was drained from SOEM's elist, or null when the ring was empty.
        private string? PopSdoErrorDetail()
        {
            if (_context == IntPtr.Zero)
                return null;

            if (!SoemNative.TryPopLastSdoError(
                    _context,
                    out ushort errSlave,
                    out ushort errIndex,
                    out byte errSub,
                    out int abortCode,
                    out SoemNative.SoemSdoErrorType errType))
            {
                return null;
            }

            string reason = SoemNative.DescribeSdoError(errType, abortCode);
            return $"[{reason} on slave={errSlave} idx=0x{errIndex:X4}:{errSub:X2}]";
        }

        public byte[] ReadInputs(int inputOffset, int inputBytes)
        {
            ValidateIoSlice(inputOffset, inputBytes);
            byte[] snapshot = new byte[inputBytes];

            lock (_ioMapLock)
            {
                Buffer.BlockCopy(_ioMap, inputOffset, snapshot, 0, inputBytes);
            }

            return snapshot;
        }

        public void ReadOutputs(int outputOffset, byte[] buffer)
        {
            ValidateIoSlice(outputOffset, buffer.Length);
            lock (_ioMapLock)
            {
                Buffer.BlockCopy(_ioMap, outputOffset, buffer, 0, buffer.Length);
            }
        }

        public void WriteOutputs(int outputOffset, ReadOnlySpan<byte> data)
        {
            ValidateIoSlice(outputOffset, data.Length);
            lock (_ioMapLock)
            {
                data.CopyTo(_ioMap.AsSpan(outputOffset));
            }
        }

        private void ApplyIoLayout(int[] outputOffsets, int[] inputOffsets, int totalBytes)
        {
            _totalIoBytes = totalBytes;

            int minOutput = int.MaxValue;
            int minInput = int.MaxValue;
            bool hasOutput = false;
            bool hasInput = false;

            for (int slave = 1; slave <= _slaveCount; slave++)
            {
                int output = outputOffsets[slave];
                int input = inputOffsets[slave];

                if (output >= 0)
                {
                    minOutput = Math.Min(minOutput, output);
                    hasOutput = true;
                }

                if (input >= 0)
                {
                    minInput = Math.Min(minInput, input);
                    hasInput = true;
                }
            }

            OutputRegionOffset = hasOutput ? minOutput : 0;
            InputRegionOffset = hasInput ? minInput : totalBytes;

            if (hasOutput && hasInput && InputRegionOffset >= OutputRegionOffset)
            {
                OutputBytes = InputRegionOffset - OutputRegionOffset;
            }
            else if (hasOutput)
            {
                OutputBytes = Math.Max(0, totalBytes - OutputRegionOffset);
            }
            else
            {
                OutputBytes = 0;
            }

            InputBytes = hasInput
                ? Math.Max(0, totalBytes - InputRegionOffset)
                : 0;
        }

        private int UpdateIoOnce()
        {
            lock (_nativeLock)
            {
                EnsureContextOpen();
                lock (_ioMapLock)
                {
                    int result = SoemNative.UpdateIo(_context, out long dcTime);
                    Interlocked.Exchange(ref _dcTime, dcTime);
                    return result;
                }
            }
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

        private void CyclicLoop()
        {
            // Raise Windows timer resolution to 1ms so Thread.Sleep(1) actually sleeps ~1ms
            // instead of the default ~15ms granularity.
            timeBeginPeriod(1);
            try
            {
                while (_running)
                {
                    try
                    {
                        UpdateIoOnce();
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref _cyclicFault, ex);
                        _running = false;
                        break;
                    }

                    Thread.Sleep(_cyclePeriodMs);
                }
            }
            finally
            {
                timeEndPeriod(1);
            }
        }

        private void EnsureContextOpen()
        {
            if (_context == IntPtr.Zero)
                throw new InvalidOperationException("SOEM bridge context is not open.");
        }

        private void ConfigureDcSync0IfAvailable()
        {
            long cycleNsLong = (long)_cyclePeriodMs * 1_000_000L;
            uint cycleNs = (uint)Math.Min(uint.MaxValue, Math.Max(1L, cycleNsLong));

            _dcSync0ConfiguredSlaves = SoemNative.TryConfigureDcSync0(_context, cycleNs, 0, out int configuredSlaves)
                ? configuredSlaves
                : 0;
        }

        private string DescribeCyclicWkc()
        {
            if (_context == IntPtr.Zero)
                return "cyclic_wkc=n/a (context closed).";

            if (!SoemNative.TryGetCyclicWkc(_context, out int lastWkc, out int expectedWkc))
                return "cyclic_wkc=n/a (rebuild soem_bridge.dll to export GetCyclicWkc).";

            string dcPart = _dcSync0ConfiguredSlaves > 0
                ? $" dc_sync0={_dcSync0ConfiguredSlaves} slave(s)."
                : " dc_sync0=not configured/no DC slave detected.";

            if (expectedWkc > 0 && lastWkc < expectedWkc)
            {
                return $"cyclic_wkc={lastWkc}/{expectedWkc} (PDO frames are not fully ACKed).{dcPart}";
            }

            if (expectedWkc > 0)
            {
                return $"cyclic_wkc={lastWkc}/{expectedWkc} (PDO frames are ACKed).{dcPart}";
            }

            return $"cyclic_wkc={lastWkc}/n/a.{dcPart}";
        }

        private string DescribeSlaveStates()
        {
            if (_context == IntPtr.Zero || _slaveCount <= 0)
                return string.Empty;

            try
            {
                SoemNative.ReadState(_context);
            }
            catch
            {
                return string.Empty;
            }

            var parts = new List<string>(_slaveCount);
            for (int slave = 1; slave <= _slaveCount; slave++)
            {
                ushort state = SoemNative.GetState(_context, slave);
                ushort baseState = (ushort)(state & 0x0F);
                bool hasErrorBit = (state & SoemNative.EC_STATE_ERROR) != 0;
                string stateName = DecodeStateName(baseState);
                string errorSuffix = hasErrorBit ? "+ERROR" : string.Empty;

                string alPart;
                if (SoemNative.TryGetAlStatusCode(_context, slave, out ushort alStatus))
                {
                    string reason = DecodeAlStatusCode(alStatus);
                    alPart = alStatus == 0
                        ? " alStatus=0x0000 (no error)"
                        : $" alStatus=0x{alStatus:X4} ({reason})";
                }
                else
                {
                    alPart = " alStatus=n/a (rebuild soem_bridge.dll to export GetAlStatusCode)";
                }

                parts.Add($"slave {slave}: state={stateName}{errorSuffix} (0x{state:X2}){alPart}");
            }

            string ioSummary = $"Master IO: total={_totalIoBytes}B out={OutputBytes}B in={InputBytes}B. ";
            return $"{ioSummary}Per-slave: {string.Join(" | ", parts)}.";
        }

        private static string DecodeStateName(ushort baseState) => baseState switch
        {
            SoemNative.EC_STATE_NONE        => "NONE",
            SoemNative.EC_STATE_INIT        => "INIT",
            SoemNative.EC_STATE_PRE_OP      => "PRE-OP",
            SoemNative.EC_STATE_BOOT        => "BOOT",
            SoemNative.EC_STATE_SAFE_OP     => "SAFE-OP",
            SoemNative.EC_STATE_OPERATIONAL => "OP",
            _                               => $"0x{baseState:X2}"
        };

        private static string DecodeAlStatusCode(ushort code) => code switch
        {
            0x0000 => "no error",
            0x0001 => "unspecified error",
            0x0010 => "invalid device setup",
            0x0011 => "invalid requested state change",
            0x0012 => "unknown requested state",
            0x0013 => "bootstrap not supported",
            0x0014 => "no valid firmware",
            0x0015 => "invalid mailbox configuration (bootstrap)",
            0x0016 => "invalid mailbox configuration (standard)",
            0x0017 => "invalid sync manager configuration (SM size violates EEPROM range or SM type wrong)",
            0x0018 => "no valid inputs available",
            0x0019 => "no valid outputs",
            0x001A => "synchronisation error",
            0x001B => "sync manager watchdog",
            0x001C => "invalid sync manager types",
            0x001D => "invalid output configuration (RxPDO/SM2 mismatch)",
            0x001E => "invalid input configuration (TxPDO/SM3 mismatch)",
            0x001F => "invalid watchdog configuration",
            0x0020 => "slave needs cold start",
            0x0021 => "slave needs INIT",
            0x0022 => "slave needs PRE-OP",
            0x0023 => "slave needs SAFE-OP",
            0x0024 => "invalid input mapping",
            0x0025 => "invalid output mapping",
            0x0026 => "inconsistent settings",
            0x002D => "invalid output FMMU configuration",
            0x002E => "invalid input FMMU configuration",
            0x0030 => "invalid DC SYNC configuration",
            0x0031 => "invalid DC LATCH configuration",
            0x0032 => "PLL error",
            0x0033 => "DC sync I/O error",
            0x0034 => "DC sync timeout error",
            0x0035 => "DC invalid sync cycle time",
            0x0036 => "DC sync0 cycle time invalid",
            0x0037 => "DC sync1 cycle time invalid",
            0x0042 => "EEPROM no access",
            0x0043 => "EEPROM error",
            0x004A => "slave restarted locally",
            0x0050 => "device identification value updated",
            _      => $"see ETG.1000.6 §0x{code:X4}"
        };

        /// <summary>
        /// One-shot diagnostic snapshot taken after the cyclic loop has had a chance
        /// to populate the IO map. Captures per-slave AL state and the raw bytes of
        /// the input and output regions so PDO mapping mismatches can be diagnosed
        /// without a debugger.
        /// </summary>
        /// <param name="maxRegionDumpBytes">Cap on bytes dumped per region (truncated with "…").</param>
        public string GetDiagnosticsReport(int maxRegionDumpBytes = 64)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[EtherCAT-Diag] SlaveCount={_slaveCount} TotalIoBytes={_totalIoBytes} " +
                          $"OutRegion=[off={OutputRegionOffset},bytes={OutputBytes}] " +
                          $"InRegion=[off={InputRegionOffset},bytes={InputBytes}]");

            if (_context != IntPtr.Zero && _slaveCount > 0)
            {
                try { SoemNative.ReadState(_context); } catch { /* best effort */ }

                for (int s = 1; s <= _slaveCount; s++)
                {
                    ushort state = SoemNative.GetState(_context, s);
                    string alPart = SoemNative.TryGetAlStatusCode(_context, s, out ushort alStatus)
                        ? $" alStatus=0x{alStatus:X4}"
                        : string.Empty;
                    string stateName = state switch
                    {
                        SoemNative.EC_STATE_INIT        => "INIT",
                        SoemNative.EC_STATE_PRE_OP      => "PRE-OP",
                        SoemNative.EC_STATE_BOOT        => "BOOT",
                        SoemNative.EC_STATE_SAFE_OP     => "SAFE-OP",
                        SoemNative.EC_STATE_OPERATIONAL => "OP",
                        _                               => $"0x{state:X2}"
                    };
                    sb.AppendLine($"[EtherCAT-Diag] Slave {s}: state={stateName} (0x{state:X2}){alPart}");
                }
            }

            if (InputBytes > 0)
            {
                int dumpBytes = Math.Min(InputBytes, Math.Max(0, maxRegionDumpBytes));
                byte[] inputs = ReadInputs(InputRegionOffset, dumpBytes);
                string suffix = dumpBytes < InputBytes ? $"… ({InputBytes - dumpBytes} more)" : "";
                sb.AppendLine($"[EtherCAT-Diag] Input  region first {dumpBytes} bytes: {Convert.ToHexString(inputs)} {suffix}");
            }

            if (OutputBytes > 0)
            {
                int dumpBytes = Math.Min(OutputBytes, Math.Max(0, maxRegionDumpBytes));
                byte[] outputs = new byte[dumpBytes];
                ReadOutputs(OutputRegionOffset, outputs);
                string suffix = dumpBytes < OutputBytes ? $"… ({OutputBytes - dumpBytes} more)" : "";
                sb.AppendLine($"[EtherCAT-Diag] Output region first {dumpBytes} bytes: {Convert.ToHexString(outputs)} {suffix}");
            }

            return sb.ToString().TrimEnd();
        }

        private void ValidateIoSlice(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > _ioMap.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"IO map slice is out of range. offset={offset}, length={length}, ioMap={_ioMap.Length}");
            }
        }

        private void EnsureIoMapPinned()
        {
            if (_ioMapHandle.IsAllocated)
                return;

            _ioMapHandle = GCHandle.Alloc(_ioMap, GCHandleType.Pinned);
            _ioMapPtr = _ioMapHandle.AddrOfPinnedObject();
        }

        private void ReleaseIoMap()
        {
            if (_ioMapHandle.IsAllocated)
                _ioMapHandle.Free();

            _ioMapPtr = IntPtr.Zero;
            _slaveCount = 0;
            _totalIoBytes = 0;
            OutputRegionOffset = 0;
            InputRegionOffset = 0;
            OutputBytes = 0;
            InputBytes = 0;
            Interlocked.Exchange(ref _dcTime, 0);
        }

        private void CloseNativeSession(bool requestInitState)
        {
            // Hold _nativeLock for the entire shutdown so FreeContext cannot
            // race the cyclic thread if the 2-second Join in CloseAsync timed out.
            lock (_nativeLock)
            {
                IntPtr context = _context;
                if (context == IntPtr.Zero)
                    return;

                try
                {
                    if (requestInitState)
                    {
                        try { SoemNative.RequestCommonState(context, SoemNative.EC_STATE_INIT); }
                        catch { }
                    }
                }
                finally
                {
                    if (_scanInfoBuffer != IntPtr.Zero)
                    {
                        SoemNative.Free(_scanInfoBuffer);
                        _scanInfoBuffer = IntPtr.Zero;
                    }

                    SoemNative.FreeContext(context);
                    _context = IntPtr.Zero;  // causes EnsureContextOpen to throw on next cyclic tick
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _running = false;
            _cyclicThread?.Join(TimeSpan.FromSeconds(2));
            _cyclicThread = null;

            try
            {
                CloseNativeSession(requestInitState: false);
            }
            catch
            {
            }

            ReleaseIoMap();
            GC.SuppressFinalize(this);
        }
    }
}
