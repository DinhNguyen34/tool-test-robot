using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ModuleMotor.Cia402.Ethercat.Soem
{
    internal static class SoemNative
    {
        private const string SoemDll = "soem_bridge";
        private const string SoemDllFileName = "soem_bridge.dll";
        private static readonly string[] RequiredExports =
        {
            "CreateContext",
            "FreeContext",
            "Free",
            "ScanDevices",
            "ConfigureIoMap",
            "GetProcessIo",
            "GetState",
            "ReadState",
            "RequestCommonState",
            "RequestState",
            "UpdateIo",
            "NoCaSdoRead",
            "ecx_SDOwrite"
        };
        private static readonly object ExportLock = new();
        private static IntPtr _soemLibraryHandle;
        private static readonly HashSet<NetworkInterfaceType> IpConfigVisibleAdapterTypes =
        [
            NetworkInterfaceType.Ethernet,
            NetworkInterfaceType.Ethernet3Megabit,
            NetworkInterfaceType.FastEthernetFx,
            NetworkInterfaceType.FastEthernetT,
            NetworkInterfaceType.GigabitEthernet,
            NetworkInterfaceType.Wireless80211
        ];

        static SoemNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(SoemNative).Assembly, ResolveSoemLibrary);
        }

        [DllImport(SoemDll, EntryPoint = "CreateContext", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CreateContext();

        [DllImport(SoemDll, EntryPoint = "FreeContext", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeContext(IntPtr context);

        [DllImport(SoemDll, EntryPoint = "Free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr pointer);

        [DllImport(SoemDll, EntryPoint = "ScanDevices", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ScanDevices(
            IntPtr context,
            string interfaceName,
            out IntPtr slaveInfoBuffer,
            out int slaveCount);

        [DllImport(SoemDll, EntryPoint = "ConfigureIoMap", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ConfigureIoMapNative(
            IntPtr context,
            IntPtr ioMap,
            IntPtr outputOffsets,
            IntPtr inputOffsets,
            out int totalBytes);

        [DllImport(SoemDll, EntryPoint = "GetProcessIo", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetProcessIo(
            IntPtr context,
            int slaveIndex,
            out IntPtr outputPointer,
            out IntPtr inputPointer);

        [DllImport(SoemDll, EntryPoint = "GetState", CallingConvention = CallingConvention.Cdecl)]
        public static extern ushort GetState(IntPtr context, int slaveIndex);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort GetAlStatusCodeDelegate(IntPtr context, int slaveIndex);

        private static GetAlStatusCodeDelegate? _getAlStatusCodeDelegate;
        private static bool _getAlStatusCodeProbed;

        /// <summary>
        /// Optional export — present only on bridges built after the OP-transition fix.
        /// Returns false on older bridges; managed callers must handle missing data.
        /// </summary>
        public static bool TryGetAlStatusCode(IntPtr context, int slaveIndex, out ushort alStatus)
        {
            if (!_getAlStatusCodeProbed)
            {
                lock (ExportLock)
                {
                    if (!_getAlStatusCodeProbed)
                    {
                        if (_soemLibraryHandle != IntPtr.Zero
                            && NativeLibrary.TryGetExport(_soemLibraryHandle, "GetAlStatusCode", out IntPtr ptr))
                        {
                            _getAlStatusCodeDelegate = Marshal.GetDelegateForFunctionPointer<GetAlStatusCodeDelegate>(ptr);
                        }

                        _getAlStatusCodeProbed = true;
                    }
                }
            }

            if (_getAlStatusCodeDelegate == null)
            {
                alStatus = 0;
                return false;
            }

            alStatus = _getAlStatusCodeDelegate(context, slaveIndex);
            return true;
        }

        [DllImport(SoemDll, EntryPoint = "ReadState", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ReadState(IntPtr context);

        [DllImport(SoemDll, EntryPoint = "RequestCommonState", CallingConvention = CallingConvention.Cdecl)]
        public static extern int RequestCommonState(IntPtr context, ushort requestedState);

        [DllImport(SoemDll, EntryPoint = "RequestState", CallingConvention = CallingConvention.Cdecl)]
        public static extern int RequestState(IntPtr context, int slaveIndex, ushort requestedState);

        [DllImport(SoemDll, EntryPoint = "UpdateIo", CallingConvention = CallingConvention.Cdecl)]
        public static extern int UpdateIo(IntPtr context, out long dcTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ConfigureDcSync0Delegate(IntPtr context, uint cycleTimeNs, int cycleShiftNs);

        private static ConfigureDcSync0Delegate? _configureDcSync0Delegate;
        private static bool _configureDcSync0Probed;

        public static bool TryConfigureDcSync0(IntPtr context, uint cycleTimeNs, int cycleShiftNs, out int configuredSlaves)
        {
            configuredSlaves = 0;
            if (!_configureDcSync0Probed)
            {
                lock (ExportLock)
                {
                    if (!_configureDcSync0Probed)
                    {
                        if (_soemLibraryHandle != IntPtr.Zero
                            && NativeLibrary.TryGetExport(_soemLibraryHandle, "ConfigureDcSync0", out IntPtr ptr))
                        {
                            _configureDcSync0Delegate = Marshal.GetDelegateForFunctionPointer<ConfigureDcSync0Delegate>(ptr);
                        }

                        _configureDcSync0Probed = true;
                    }
                }
            }

            if (_configureDcSync0Delegate == null)
                return false;

            configuredSlaves = _configureDcSync0Delegate(context, cycleTimeNs, cycleShiftNs);
            return configuredSlaves >= 0;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetCyclicWkcDelegate(IntPtr context, out int lastWkc, out int expectedWkc);

        private static GetCyclicWkcDelegate? _getCyclicWkcDelegate;
        private static bool _getCyclicWkcProbed;

        public static bool TryGetCyclicWkc(IntPtr context, out int lastWkc, out int expectedWkc)
        {
            lastWkc = 0;
            expectedWkc = 0;
            if (!_getCyclicWkcProbed)
            {
                lock (ExportLock)
                {
                    if (!_getCyclicWkcProbed)
                    {
                        if (_soemLibraryHandle != IntPtr.Zero
                            && NativeLibrary.TryGetExport(_soemLibraryHandle, "GetCyclicWkc", out IntPtr ptr))
                        {
                            _getCyclicWkcDelegate = Marshal.GetDelegateForFunctionPointer<GetCyclicWkcDelegate>(ptr);
                        }

                        _getCyclicWkcProbed = true;
                    }
                }
            }

            if (_getCyclicWkcDelegate == null)
                return false;

            return _getCyclicWkcDelegate(context, out lastWkc, out expectedWkc) > 0;
        }

        /// <summary>
        /// Caller must allocate at least 8 bytes in <paramref name="valueBuffer"/>.
        /// The bridge writes at most sizeof(the requested CiA 402 object) bytes — all
        /// standard CiA 402 objects are ≤4 bytes; the 8-byte buffer provides a safe margin.
        /// </summary>
        [DllImport(SoemDll, EntryPoint = "NoCaSdoRead", CallingConvention = CallingConvention.Cdecl)]
        public static extern int NoCaSdoRead(
            IntPtr context,
            ushort slave,
            ushort index,
            byte subIndex,
            IntPtr valueBuffer);

        [DllImport(SoemDll, EntryPoint = "ecx_SDOwrite", CallingConvention = CallingConvention.Cdecl)]
        public static extern int EcxSdoWrite(
            IntPtr context,
            ushort slave,
            ushort index,
            byte subIndex,
            [MarshalAs(UnmanagedType.I1)] bool completeAccess,
            int size,
            IntPtr valueBuffer,
            int timeout);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PopLastSdoErrorDelegate(
            IntPtr context,
            out ushort slave,
            out ushort index,
            out byte subIndex,
            out int abortCode,
            out int errorType);

        private static PopLastSdoErrorDelegate? _popLastSdoErrorDelegate;
        private static bool _popLastSdoErrorProbed;

        /// <summary>
        /// Drains SOEM's internal error ring and returns the most recent CoE/mailbox
        /// error so callers can surface SDO abort codes (which are otherwise invisible:
        /// <c>ecx_SDOread/write</c> returns WKC=0 on slave-aborted requests but the
        /// reason is only in <c>context->elist</c>). Returns false on older bridges
        /// that don't export <c>PopLastSdoError</c>.
        /// </summary>
        public static bool TryPopLastSdoError(
            IntPtr context,
            out ushort slave,
            out ushort index,
            out byte subIndex,
            out int abortCode,
            out SoemSdoErrorType errorType)
        {
            slave = 0;
            index = 0;
            subIndex = 0;
            abortCode = 0;
            errorType = SoemSdoErrorType.SdoAbort;

            if (!_popLastSdoErrorProbed)
            {
                lock (ExportLock)
                {
                    if (!_popLastSdoErrorProbed)
                    {
                        if (_soemLibraryHandle != IntPtr.Zero
                            && NativeLibrary.TryGetExport(_soemLibraryHandle, "PopLastSdoError", out IntPtr ptr))
                        {
                            _popLastSdoErrorDelegate = Marshal.GetDelegateForFunctionPointer<PopLastSdoErrorDelegate>(ptr);
                        }

                        _popLastSdoErrorProbed = true;
                    }
                }
            }

            if (_popLastSdoErrorDelegate == null)
                return false;

            int rawType;
            int popped = _popLastSdoErrorDelegate(context, out slave, out index, out subIndex, out abortCode, out rawType);
            errorType = (SoemSdoErrorType)rawType;
            return popped != 0;
        }

        public static int ConfigureIoMap(
            IntPtr context,
            IntPtr ioMap,
            int[] outputOffsets,
            int[] inputOffsets,
            out int totalBytes)
        {
            if (outputOffsets.Length == 0)
                throw new ArgumentException("Output offset buffer must not be empty.", nameof(outputOffsets));
            if (inputOffsets.Length == 0)
                throw new ArgumentException("Input offset buffer must not be empty.", nameof(inputOffsets));

            GCHandle outputHandle = default;
            GCHandle inputHandle = default;

            try
            {
                outputHandle = GCHandle.Alloc(outputOffsets, GCHandleType.Pinned);
                inputHandle = GCHandle.Alloc(inputOffsets, GCHandleType.Pinned);
                return ConfigureIoMapNative(
                    context,
                    ioMap,
                    outputHandle.AddrOfPinnedObject(),
                    inputHandle.AddrOfPinnedObject(),
                    out totalBytes);
            }
            finally
            {
                if (inputHandle.IsAllocated)
                    inputHandle.Free();
                if (outputHandle.IsAllocated)
                    outputHandle.Free();
            }
        }

        public static bool TryEnsureLoaded(out string errorMessage)
        {
            lock (ExportLock)
            {
                if (_soemLibraryHandle != IntPtr.Zero)
                {
                    errorMessage = string.Empty;
                    return true;
                }

                if (!TryLoadSoemLibrary(out _soemLibraryHandle))
                {
                    string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                    errorMessage =
                        $"SOEM bridge was not found. Expected '{SoemDllFileName}' in '{Path.Combine(baseDirectory, "native")}' or '{baseDirectory}', and it must match the app architecture (x64).";
                    return false;
                }

                string[] missingExports = RequiredExports
                    .Where(exportName => !NativeLibrary.TryGetExport(_soemLibraryHandle, exportName, out _))
                    .ToArray();

                if (missingExports.Length > 0)
                {
                    errorMessage =
                        $"Loaded '{SoemDllFileName}', but it is not the wrapper build expected by this app. Missing exports: {string.Join(", ", missingExports)}.";
                    return false;
                }

                errorMessage = string.Empty;
                return true;
            }
        }

        public static IReadOnlyList<string> GetKnownAdapterNames()
            => FindAdapters().Select(adapter => adapter.Name).ToList();

        public static IReadOnlyList<SoemNetworkAdapter> FindAdapters()
            => FindAdaptersFromNetworkInterfaces();

        public static string GetNpcapOpenFailureHint()
        {
            if (TryGetNpcapAdminOnlyError(out string adminOnlyError))
                return adminOnlyError;

            return "Verify the selected NIC is the correct EtherCAT adapter and that Npcap Packet Driver is bound to it.";
        }

        public static bool TryGetNpcapAdminOnlyError(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!IsNpcapAdminOnlyMode())
                return false;

            if (IsProcessElevated())
                return false;

            errorMessage =
                "Npcap is installed in AdminOnly mode and the current process is not elevated. Run the app as Administrator or reinstall Npcap with non-admin capture enabled.";
            return true;
        }

        private static IReadOnlyList<SoemNetworkAdapter> FindAdaptersFromNetworkInterfaces()
        {
            List<SoemNetworkAdapter> adapters = new();

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()
                         .OrderBy(nic => nic.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!IpConfigVisibleAdapterTypes.Contains(nic.NetworkInterfaceType))
                    continue;

                if (nic.OperationalStatus == OperationalStatus.NotPresent)
                    continue;

                if (string.IsNullOrWhiteSpace(nic.Name) || string.IsNullOrWhiteSpace(nic.Id))
                    continue;

                string[] ipv4Addresses = nic.GetIPProperties()
                    .UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                string id = nic.Id.Trim('{', '}').ToUpperInvariant();
                string name = $@"\Device\NPF_{{{id}}}";
                string statusText = nic.OperationalStatus == OperationalStatus.Up
                    ? "Connected"
                    : "Media disconnected";

                adapters.Add(new SoemNetworkAdapter(
                    name,
                    nic.Name,
                    nic.Description,
                    statusText,
                    ipv4Addresses));
            }

            return adapters;
        }

        private static bool IsProcessElevated()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNpcapAdminOnlyMode()
        {
            try
            {
                using RegistryKey? parameters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap\Parameters");
                object? adminOnlyValue = parameters?.GetValue("AdminOnly");
                return adminOnlyValue is int adminOnly && adminOnly == 1;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr ResolveSoemLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, SoemDll,         StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(libraryName, SoemDllFileName, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            return TryLoadSoemLibrary(out IntPtr handle)
                ? handle
                : IntPtr.Zero;
        }

        private static bool TryLoadSoemLibrary(out IntPtr handle)
        {
            handle = IntPtr.Zero;

            foreach (string candidatePath in GetCandidateLibraryPaths())
            {
                if (!File.Exists(candidatePath))
                    continue;

                if (NativeLibrary.TryLoad(candidatePath, out handle))
                    return true;
            }

            return NativeLibrary.TryLoad(SoemDll, out handle) ||
                   NativeLibrary.TryLoad(SoemDllFileName, out handle);
        }

        private static IEnumerable<string> GetCandidateLibraryPaths()
        {
            string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            yield return Path.Combine(baseDirectory, "native", SoemDllFileName);
            yield return Path.Combine(baseDirectory, SoemDllFileName);
        }

        public const ushort EC_STATE_NONE = 0x00;
        public const ushort EC_STATE_INIT = 0x01;
        public const ushort EC_STATE_PRE_OP = 0x02;
        public const ushort EC_STATE_BOOT = 0x03;
        public const ushort EC_STATE_SAFE_OP = 0x04;
        public const ushort EC_STATE_OPERATIONAL = 0x08;
        public const ushort EC_STATE_ERROR = 0x10;
        /// <summary>OR with target state to acknowledge an error — same bit as EC_STATE_ERROR per SOEM convention.</summary>
        public const ushort EC_STATE_ACK   = 0x10;

        public const int EC_TIMEOUTRET = 2_000;
        public const int EC_TIMEOUTRXM = 700_000;
        public const int EC_TIMEOUTSTATE = 2_000_000;
        public const int EC_TIMEOUTRET3 = 2_000;

        /// <summary>Mirrors SOEM's <c>ec_err_type</c> (ec_type.h).</summary>
        public enum SoemSdoErrorType
        {
            SdoAbort = 0,
            Emergency = 1,
            PacketError = 3,
            SdoInfoError = 4,
            FoeError = 5,
            FoeBuf2Small = 6,
            FoePacketNumber = 7,
            SoeError = 8,
            MailboxError = 9,
            FoeFileNotFound = 10,
            EoeInvalidRxData = 11,
        }

        /// <summary>
        /// Decodes a SOEM error returned by <see cref="TryPopLastSdoError"/> into a
        /// short human-readable phrase. Includes the standard CiA 301 SDO abort codes
        /// that show up in practice when programming PDO mapping objects.
        /// </summary>
        public static string DescribeSdoError(SoemSdoErrorType errorType, int abortCode)
        {
            uint code = unchecked((uint)abortCode);

            if (errorType != SoemSdoErrorType.SdoAbort && errorType != SoemSdoErrorType.SdoInfoError)
                return $"{errorType} (raw=0x{code:X8})";

            string reason = code switch
            {
                0x05030000 => "toggle bit not alternated",
                0x05040000 => "SDO protocol timed out",
                0x05040001 => "client/server command specifier not valid or unknown",
                0x05040005 => "out of memory",
                0x06010000 => "unsupported access to an object",
                0x06010001 => "attempt to read a write-only object",
                0x06010002 => "attempt to write a read-only object",
                0x06010003 => "subindex cannot be written, SI0 must be 0 for write access",
                0x06010004 => "SDO complete access not supported for this object",
                0x06010005 => "object length exceeds mailbox size",
                0x06010006 => "object mapped to RxPDO, SDO download blocked",
                0x06020000 => "object does not exist in the object dictionary",
                0x06040041 => "object cannot be mapped to PDO",
                0x06040042 => "PDO length would be exceeded by mapped objects",
                0x06040043 => "general parameter incompatibility reason",
                0x06040047 => "general internal incompatibility in the device",
                0x06060000 => "access failed due to a hardware error",
                0x06070010 => "data type mismatch — service parameter length mismatch",
                0x06070012 => "data type mismatch — service parameter too long",
                0x06070013 => "data type mismatch — service parameter too short",
                0x06090011 => "subindex does not exist",
                0x06090030 => "value range of parameter exceeded (write only)",
                0x06090031 => "value of parameter written too high",
                0x06090032 => "value of parameter written too low",
                0x06090036 => "maximum value is less than minimum value",
                0x08000000 => "general error",
                0x08000020 => "data cannot be transferred or stored to the application",
                0x08000021 => "data cannot be transferred — local control",
                0x08000022 => "data cannot be transferred — present device state",
                0x08000023 => "object dictionary not present or generation fails",
                _          => "unknown SDO abort",
            };

            return $"abort=0x{code:X8} ({reason})";
        }
    }
}
