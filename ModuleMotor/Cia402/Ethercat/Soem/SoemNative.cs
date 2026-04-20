using System.Runtime.InteropServices;

namespace ModuleMotor.Cia402.Ethercat.Soem
{
    /// <summary>
    /// P/Invoke declarations for the SOEM (Simple Open EtherCAT Master) native library.
    /// Requires soem.dll (Windows) built from https://github.com/OpenEtherCATsociety/SOEM
    /// and Npcap (or WinPcap) installed for raw-socket access.
    /// Place soem.dll next to the application executable.
    /// </summary>
    internal static class SoemNative
    {
        private const string SoemDll = "soem";

        // ── Master lifecycle ──────────────────────────────────────────────────

        /// <summary>Initialize the EtherCAT master on the given network interface.</summary>
        /// <param name="ifname">NIC name, e.g. "\\Device\\NPF_{GUID}" on Windows.</param>
        /// <returns>1 on success, 0 on failure.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_init", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Init(string ifname);

        /// <summary>Scan the bus and detect slaves.</summary>
        /// <param name="usetable">0 = do not use pre-configured slave table.</param>
        /// <returns>Number of slaves found.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_config_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ConfigInit(byte usetable);

        /// <summary>Configure PDO mapping and build the IO map.</summary>
        /// <param name="iomap">Pointer to a caller-allocated IO map buffer (pinned byte[]).</param>
        /// <returns>Total IO map size in bytes.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_config_map", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ConfigMap(IntPtr iomap);

        /// <summary>Configure Distributed Clocks (optional; improves synchronisation).</summary>
        [DllImport(SoemDll, EntryPoint = "ec_configdc", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ConfigDc();

        /// <summary>Release all SOEM resources and close the NIC.</summary>
        [DllImport(SoemDll, EntryPoint = "ec_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close();

        // ── State machine ────────────────────────────────────────────────────

        /// <summary>Write requested state to one or all (slave=0) slaves.</summary>
        [DllImport(SoemDll, EntryPoint = "ec_writestate", CallingConvention = CallingConvention.Cdecl)]
        public static extern int WriteState(ushort slave);

        /// <summary>Read the current state of all slaves.</summary>
        [DllImport(SoemDll, EntryPoint = "ec_readstate", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ReadState();

        /// <summary>
        /// Busy-wait until slave (0 = all) reaches <paramref name="reqstate"/>
        /// or the timeout (µs) expires.
        /// </summary>
        /// <returns>Lowest state observed across all queried slaves.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_statecheck", CallingConvention = CallingConvention.Cdecl)]
        public static extern ushort StateCheck(ushort slave, ushort reqstate, int timeout);

        // ── Process data ─────────────────────────────────────────────────────

        /// <summary>Queue and send the current output process data frame.</summary>
        [DllImport(SoemDll, EntryPoint = "ec_send_processdata", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SendProcessdata();

        /// <summary>Receive the input process data frame (blocks up to <paramref name="timeout"/> µs).</summary>
        /// <returns>Received data size in bytes, or 0 on timeout.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_receive_processdata", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ReceiveProcessdata(int timeout);

        // ── SDO mailbox ───────────────────────────────────────────────────────

        /// <summary>
        /// CoE SDO read — reads object <paramref name="index"/>:<paramref name="subindex"/>
        /// from slave into the caller's buffer <paramref name="p"/>.
        /// </summary>
        /// <param name="slave">Slave index (1-based).</param>
        /// <param name="psize">In: buffer capacity; Out: bytes actually read.</param>
        /// <param name="p">Buffer pointer (must be at least *psize bytes).</param>
        /// <param name="timeout">Timeout in µs.</param>
        /// <returns>Positive workcounter on success, ≤ 0 on error.</returns>
        [DllImport(SoemDll, EntryPoint = "ec_SDOread", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SdoRead(
            ushort slave,
            ushort index,
            byte subindex,
            [MarshalAs(UnmanagedType.I1)] bool completeAccess,
            ref int psize,
            IntPtr p,
            int timeout);

        /// <summary>
        /// CoE SDO write — writes object <paramref name="index"/>:<paramref name="subindex"/>
        /// from the caller's buffer to the slave.
        /// </summary>
        [DllImport(SoemDll, EntryPoint = "ec_SDOwrite", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SdoWrite(
            ushort slave,
            ushort index,
            byte subindex,
            [MarshalAs(UnmanagedType.I1)] bool completeAccess,
            int psize,
            IntPtr p,
            int timeout);

        // ── EC_STATE constants (from ethercattype.h) ──────────────────────────
        public const ushort EC_STATE_NONE        = 0x00;
        public const ushort EC_STATE_INIT        = 0x01;
        public const ushort EC_STATE_PRE_OP      = 0x02;
        public const ushort EC_STATE_BOOT        = 0x03;
        public const ushort EC_STATE_SAFE_OP     = 0x04;
        public const ushort EC_STATE_OPERATIONAL = 0x08;
        public const ushort EC_STATE_ERROR       = 0x10;
        public const ushort EC_STATE_ACK         = 0x10;

        // ── Timeout constants (µs) ────────────────────────────────────────────
        /// <summary>SDO response retry timeout: 2 ms.</summary>
        public const int EC_TIMEOUTRET   = 2_000;
        /// <summary>RX mailbox timeout: 700 ms.</summary>
        public const int EC_TIMEOUTRXM  = 700_000;
        /// <summary>State-change timeout: 2 s.</summary>
        public const int EC_TIMEOUTSTATE = 2_000_000;
        /// <summary>Receive process-data timeout per cycle: 2 ms.</summary>
        public const int EC_TIMEOUTRET3  = 2_000;
    }
}
