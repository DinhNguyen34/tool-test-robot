using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ModuleMotor.Cia402.Ethercat.Soem
{
    /// <summary>
    /// Manages the SOEM EtherCAT master lifecycle and provides thread-safe
    /// SDO mailbox access and cyclic PDO process data exchange.
    /// </summary>
    public sealed class SoemMaster : IDisposable
    {
        // ── IO map ────────────────────────────────────────────────────────────
        private const int IoMapSize = 4096;
        private readonly byte[] _ioMap = new byte[IoMapSize];
        private GCHandle _ioMapHandle;
        private IntPtr _ioMapPtr;

        private readonly object _ioMapLock = new();

        // ── Configuration ─────────────────────────────────────────────────────
        private readonly string _interfaceName;
        private readonly int _cyclePeriodMs;

        // ── State ─────────────────────────────────────────────────────────────
        private volatile bool _running;
        private Thread? _cyclicThread;
        private int _slaveCount;

        /// <summary>Number of slaves detected during <see cref="OpenAsync"/>.</summary>
        public int SlaveCount => _slaveCount;

        /// <summary>
        /// Total output (master→slave) bytes in the IO map, populated after <see cref="OpenAsync"/>.
        /// </summary>
        public int OutputBytes { get; private set; }

        /// <summary>
        /// Total input (slave→master) bytes in the IO map, populated after <see cref="OpenAsync"/>.
        /// </summary>
        public int InputBytes { get; private set; }

        /// <param name="interfaceName">
        /// Windows NIC name as returned by <c>ec_find_adapters()</c>,
        /// e.g. <c>"\Device\NPF_{GUID}"</c>.
        /// </param>
        /// <param name="cyclePeriodMs">PDO cycle time in milliseconds (default 1 ms).</param>
        public SoemMaster(string interfaceName, int cyclePeriodMs = 1)
        {
            _interfaceName = interfaceName;
            _cyclePeriodMs = Math.Max(1, cyclePeriodMs);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises SOEM, scans slaves, maps process data and brings all slaves
        /// to Operational state. Starts the background cyclic PDO thread.
        /// </summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            _ioMapHandle = GCHandle.Alloc(_ioMap, GCHandleType.Pinned);
            _ioMapPtr = _ioMapHandle.AddrOfPinnedObject();

            await Task.Run(() =>
            {
                // 1. Initialise NIC
                if (SoemNative.Init(_interfaceName) == 0)
                    throw new InvalidOperationException(
                        $"SOEM: failed to open network interface '{_interfaceName}'.");

                // 2. Scan slaves
                _slaveCount = SoemNative.ConfigInit(0);
                if (_slaveCount == 0)
                    throw new InvalidOperationException(
                        "SOEM: no EtherCAT slaves found on the bus.");

                // 3. Map PDO process data
                int ioSize = SoemNative.ConfigMap(_ioMapPtr);
                if (ioSize <= 0)
                    throw new InvalidOperationException(
                        "SOEM: ec_config_map returned 0 — no process data mapped.");

                // 4. Configure Distributed Clocks (best-effort)
                SoemNative.ConfigDc();

                // 5. Transition: Init → PreOp → SafeOp
                SetState(SoemNative.EC_STATE_SAFE_OP, ct);

                // 6. Prime the PDO exchange before going Operational
                SoemNative.SendProcessdata();
                SoemNative.ReceiveProcessdata(SoemNative.EC_TIMEOUTRET3);

                // 7. Transition: SafeOp → Operational
                SetState(SoemNative.EC_STATE_OPERATIONAL, ct);

                // 8. Record IO map byte counts (first slave gives us the totals for a
                //    single-slave network; extend here if multi-slave support is needed)
                DetermineIoOffsets(ioSize);

            }, ct);

            // 9. Start cyclic PDO thread
            _running = true;
            _cyclicThread = new Thread(CyclicLoop)
            {
                Name = "SOEM-PDO",
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            _cyclicThread.Start();
        }

        /// <summary>
        /// Stops the cyclic thread, transitions all slaves back to Init and releases SOEM.
        /// </summary>
        public async Task CloseAsync()
        {
            _running = false;
            _cyclicThread?.Join(TimeSpan.FromSeconds(2));
            _cyclicThread = null;

            await Task.Run(() =>
            {
                SoemNative.WriteState(SoemNative.EC_STATE_INIT);
                SoemNative.Close();
            });

            if (_ioMapHandle.IsAllocated)
                _ioMapHandle.Free();
        }

        // ── SDO access ────────────────────────────────────────────────────────

        /// <summary>
        /// Reads an SDO object from <paramref name="slave"/> (1-based) and returns
        /// the raw bytes. Runs on a thread pool thread to avoid blocking the caller.
        /// </summary>
        public Task<byte[]> SdoReadAsync(
            ushort slave, ushort index, byte subIndex,
            int maxBytes, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                IntPtr buf = Marshal.AllocHGlobal(maxBytes);
                try
                {
                    int size = maxBytes;
                    int wc = SoemNative.SdoRead(
                        slave, index, subIndex, false,
                        ref size, buf, SoemNative.EC_TIMEOUTRXM);

                    if (wc <= 0)
                        throw new InvalidOperationException(
                            $"SOEM SDO read failed: slave={slave} idx=0x{index:X4}:{subIndex:X2} wc={wc}");

                    byte[] result = new byte[size];
                    Marshal.Copy(buf, result, 0, size);
                    return result;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }, ct);
        }

        /// <summary>
        /// Writes raw bytes to an SDO object on <paramref name="slave"/> (1-based).
        /// </summary>
        public Task SdoWriteAsync(
            ushort slave, ushort index, byte subIndex,
            byte[] data, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                IntPtr buf = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buf, data.Length);
                    int wc = SoemNative.SdoWrite(
                        slave, index, subIndex, false,
                        data.Length, buf, SoemNative.EC_TIMEOUTRXM);

                    if (wc <= 0)
                        throw new InvalidOperationException(
                            $"SOEM SDO write failed: slave={slave} idx=0x{index:X4}:{subIndex:X2} wc={wc}");
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }, ct);
        }

        // ── PDO process data ──────────────────────────────────────────────────

        /// <summary>
        /// Reads a snapshot of the current input (slave→master) PDO data.
        /// Thread-safe — copies bytes under lock so the cyclic thread cannot race.
        /// </summary>
        /// <param name="inputOffset">Byte offset in the IO map where inputs start.</param>
        /// <param name="inputBytes">Number of input bytes to copy.</param>
        public byte[] ReadInputs(int inputOffset, int inputBytes)
        {
            byte[] snapshot = new byte[inputBytes];
            lock (_ioMapLock)
                Buffer.BlockCopy(_ioMap, inputOffset, snapshot, 0, inputBytes);
            return snapshot;
        }

        /// <summary>
        /// Reads a snapshot of the current output (master→slave) PDO bytes from the IO map.
        /// Used to perform sparse updates (read-modify-write) without clobbering unchanged fields.
        /// </summary>
        public void ReadOutputs(int outputOffset, byte[] buffer)
        {
            lock (_ioMapLock)
                Buffer.BlockCopy(_ioMap, outputOffset, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Writes output (master→slave) PDO bytes into the IO map.
        /// The cyclic thread will include these in the next <c>ec_send_processdata</c> call.
        /// Thread-safe — writes under lock.
        /// </summary>
        /// <param name="outputOffset">Byte offset in the IO map where outputs start.</param>
        /// <param name="data">Bytes to write (must fit within output area).</param>
        public void WriteOutputs(int outputOffset, ReadOnlySpan<byte> data)
        {
            lock (_ioMapLock)
                data.CopyTo(_ioMap.AsSpan(outputOffset));
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void SetState(ushort targetState, CancellationToken ct)
        {
            // Write state request to all slaves (slave 0 = broadcast)
            SoemNative.ReadState();
            SoemNative.WriteState(targetState);

            ushort actual = SoemNative.StateCheck(0, targetState, SoemNative.EC_TIMEOUTSTATE);
            if (actual != targetState)
                throw new InvalidOperationException(
                    $"SOEM: slaves did not reach state 0x{targetState:X2} " +
                    $"(actual=0x{actual:X2}).");
        }

        private void DetermineIoOffsets(int totalIoSize)
        {
            // For a single-slave network SOEM lays out the IO map as:
            //   [0 .. OutputBytes-1]             → slave outputs (master writes)
            //   [OutputBytes .. totalIoSize-1]   → slave inputs  (master reads)
            //
            // For multi-slave networks the layout is sequential per slave group.
            // OutputBytes and InputBytes can be overridden via ErobPdoMap if the
            // auto-detected split is wrong.
            //
            // We approximate the split by reading from the slave config;
            // if SOEM's ec_config_map returns exactly (OutBytes + InBytes) bytes,
            // the totals should match the ErobPdoMap defaults.
            // If not, adjust ErobPdoMap.Default to match.
            OutputBytes = totalIoSize / 2;  // reasonable default; overridden by ErobPdoMap
            InputBytes  = totalIoSize - OutputBytes;
        }

        private void CyclicLoop()
        {
            while (_running)
            {
                lock (_ioMapLock)
                {
                    SoemNative.SendProcessdata();
                    SoemNative.ReceiveProcessdata(SoemNative.EC_TIMEOUTRET3);
                }

                Thread.Sleep(_cyclePeriodMs);
            }
        }

        public void Dispose()
        {
            _running = false;
            _cyclicThread?.Join(TimeSpan.FromSeconds(2));
            _cyclicThread = null;

            if (_ioMapHandle.IsAllocated)
                _ioMapHandle.Free();
        }
    }
}
