using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Media3D;

namespace ModuleLidar
{
    public enum LivoxLidarStatus : int
    {
        Success = 0,
        Failure = -1,
        Timeout = -4,
        NotConnected = -2
    }

    public enum LivoxLidarWorkMode : byte
    {
        SAMPLING = 0x01,
        IDLE = 0x02,
        ERROR = 0x04,
        SELFCHECK = 0x05,
        MOTORSTARUP = 0x06,
        UPGRADE = 0x08,
        READY = 0x09
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LivoxLidarInfo
    {
        public byte dev_type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string sn;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string lidar_ip;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DecodedPoint
    {
        public float x;
        public float y;
        public float z;
        public byte reflectivity;
        public byte tag;
        public byte confidence;
        public byte noise_type;
        public byte glue_type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DecodedDeviceStatus
    {
        public byte ret_code;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string sn;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] version_app;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] version_hw;
        public int core_temp;
        public byte work_mode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DecodedImuPoint
    {
        public float gyro_x;
        public float gyro_y;
        public float gyro_z;
        public float acc_x;
        public float acc_y;
        public float acc_z;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DecodedPointCloudCallback(
    uint handle,
    uint dot_num,
    IntPtr points,
    IntPtr client_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DecodedImuDataCallback(uint handle, ref DecodedImuPoint imu_data, IntPtr client_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DecodedInternalInfoCallback(LivoxLidarStatus status, uint handle, ref DecodedDeviceStatus response, IntPtr client_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void InfoChangeCallback(uint handle, ref LivoxLidarInfo info, IntPtr client_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AsyncControlCallback(LivoxLidarStatus status, uint handle, IntPtr response, IntPtr client_data);

    public static class LivoxApi
    {
        private const string LibName = "LivoxSdkLib";

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool InitSdk(string path);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UninitSdk();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool StartSdk();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetDecodedPointCloudCallback(DecodedPointCloudCallback cb, IntPtr client_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetDecodedImuCallback(DecodedImuDataCallback cb, IntPtr client_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetInfoChangeCallback(InfoChangeCallback cb, IntPtr client_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetWorkMode(uint handle, LivoxLidarWorkMode mode, AsyncControlCallback cb, IntPtr client_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int QueryDecodedInternalInfo(uint handle, DecodedInternalInfoCallback cb, IntPtr client_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DisableConsoleLogger();
    }
}
