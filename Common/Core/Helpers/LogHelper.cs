using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System.Text;

namespace Common.Core.Helpers
{
    public class LogHelper
    {
        private static ILog log = null;
        private static ILog? tcLog = null;
        private static ILog importLog = null;
        public readonly static string ImportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs\\ImportLog.txt");
        public readonly static string TCLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs\\TestCaseLog.txt");
        private static string tcPath = string.Empty;
        static StringBuilder tcStr = new StringBuilder();
        public static void Init()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            log = LogCtr.GetLoggerRollingFileAppender("log", string.Format("{0}/{1}", AppDomain.CurrentDomain.BaseDirectory, "/Logs/log.txt"));
            Tracking();
            Debug(AppDomain.CurrentDomain.BaseDirectory);
        }
        public static void ClearImportLog()
        {
            try
            {
                FileHelper.DeleteFile(ImportPath);
            }
            catch (Exception ex)
            {
                Exception(ex);
            }
        }


       

        public static void LogTestCase(string mes)
        {
            tcStr.AppendLine($"{DateTime.Now.ToString("HH:mm:ss.fff")}   {mes}");
        }
        public static string GetTcLog()
        {
            return tcStr.ToString();
        }
        public static void ClearTCLog()
        {
            tcStr.Clear();
        }
        public static void Tracking()
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            log.Info(string.Format("{0} - {1}", method.DeclaringType.Name, method.Name));
        }
        public static void Debug(string mes)
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            log.Debug(string.Format("{0} - {1}: {2}", method.DeclaringType.Name, method.Name, mes));
        }
        public static void LogImport(string mes)
        {
            importLog.Debug(mes);
        }

        public static void Info(string mes)
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            log.Info(string.Format("{0} - {1}: {2}", method.DeclaringType.Name, method.Name, mes));
            Console.WriteLine(string.Format("{0} - {1}: {2}", method.DeclaringType.Name, method.Name, mes));
        }
        public static void Debug(string mes, object arg0, object arg1 = null, object arg2 = null)
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            mes = string.Format(mes, arg0, arg1, arg2);
            log.Debug(string.Format("{0} - {1}: {2}", method.DeclaringType.Name, method.Name, mes));
        }


        public static void Error(string mes)
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            log.Error(string.Format("{0} - {1}: {2}", method.DeclaringType.Name, method.Name, mes));

        }

        public static void Exception(Exception ex)
        {
            var method = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod();
            log.Fatal(string.Format("[Exception]{0} - {1}: {2}", method.DeclaringType.Name, method.Name, ex.ToString()));
        }
    }

    public static class LogCtr
    {
        static LogCtr()
        {
            var hierarchy = (Hierarchy)LogManager.GetRepository();
            hierarchy.Root.Level = Level.All;
            hierarchy.Configured = true;
        }

        public static ILog GetLoggerRollingFileAppender(string logName, string fileName)
        {
            var log = LogManager.Exists(logName);

            if (log != null) return log;

            var appenderName = $"{logName}Appender";
            log = LogManager.GetLogger(logName);
            ((Logger)log.Logger).AddAppender(GetRollingFileAppender(appenderName, fileName));

            return log;
        }

        public static RollingFileAppender GetRollingFileAppender(string appenderName, string fileName)
        {
            var layout = new PatternLayout { ConversionPattern = "%date{dd.MM.yyyy HH:mm:ss.fff}  [%-5level]  %message%newline" };
            layout.ActivateOptions();

            var appender = new RollingFileAppender
            {
                Name = appenderName,
                File = fileName,
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaxSizeRollBackups = 10,
                MaximumFileSize = "30000KB",
                Layout = layout,
                ImmediateFlush = true,
                LockingModel = new FileAppender.MinimalLock(),
                Encoding = Encoding.UTF8,
            };

            appender.ActivateOptions();

            return appender;
        }
    }
}
