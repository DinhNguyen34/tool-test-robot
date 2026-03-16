using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Common.Core.Helpers
{
    public static class CommonApplicationUtilities
    {
        private static string logFolders = GetExeDirectory() + "Logs";
        public static string LogConfigs = Configs + "\\LogConfigs.json";
        public static string SnakeTail = GetExeDirectory() + "OtherApps\\SnakeTail.exe";
        public static string ConvertBlf2 = GetExeDirectory() + "OtherApps\\Logs\\lif.dll";

        public static string AppFolder = GetExeDirectory();
        public static string Modules = GetExeDirectory() + "Modules";
        public static string OtherApps { get; } = GetExeDirectory() + "OtherApps";
        public static string USER_NAME
        {
            get
            {
                return WindowsIdentity.GetCurrent().Name;
            }
        }

        public static string USER_NAME_SIMPLE
        {
            get
            {
                try
                {
                    string str = WindowsIdentity.GetCurrent().Name;
                    if (str.Contains("\\"))
                        str = str.Substring(str.LastIndexOf('\\') + 1);
                    return str;
                }
                catch
                {

                }
                return "";
            }
        }

        public static string MACHINE_NAME
        {
            get
            {
                return Environment.MachineName;
            }
        }
       
        public static string GetIPAddress()
        {
            IPAddress[] hostAddresses = Dns.GetHostAddresses("");

            foreach (IPAddress hostAddress in hostAddresses)
            {
                if (hostAddress.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(hostAddress) &&  // ignore loopback addresses
                    !hostAddress.ToString().StartsWith("169.254."))  // ignore link-local addresses
                    return hostAddress.ToString();
            }
            return string.Empty; // or IPAddress.None if you prefer
        }

        public static string GetExeDirectory()
        {
            try
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                //MessageBox.Show(ex.ToString());
            }
            return string.Empty;
        }

        
        public static string LogFolders
        {
            get
            {
                return CommonApplicationUtilities.logFolders;
            }
        }
       
       
        public static string CanLog
        {
            get
            {
                string canlog = string.Format("{0}CanLog", GetExeDirectory());

                if (!Directory.Exists(canlog))
                {
                    Directory.CreateDirectory(canlog);
                }
                return canlog;
            }
        }
        public static string Configs
        {
            get
            {
                LogHelper.Debug(GetExeDirectory());
                string configs = string.Format("{0}Configs", GetExeDirectory());

                if (!Directory.Exists(configs))
                {
                    Directory.CreateDirectory(configs);
                }
                return configs;
            }
        }
       

    }
}
