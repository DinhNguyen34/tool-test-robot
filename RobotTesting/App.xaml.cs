using Common.Core.Helpers;
using ModuleCover;
using ModuleMotor;
using ModuleNetwork;
using ModuleTestLed;
using Prism.Unity;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Resources;
using System.Windows;

namespace RobotTesting
{
    public partial class App : Application
    {
       
        protected override void OnStartup(StartupEventArgs e)
        {
            LogHelper.Init();
            // Bắt exception từ UI thread
            this.DispatcherUnhandledException += (s, ex) =>
            {
                LogHelper.Exception(ex.Exception);
                ex.Handled = true; // hoặc false để crash bình thường
            };

            // Bắt exception từ async Task
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                LogHelper.Exception(ex.Exception);
                ex.SetObserved();
            };

            // Bắt exception từ các thread khác
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                if (ex.ExceptionObject != null)
                    LogHelper.Debug(ex.ExceptionObject.ToString());
            };


            base.OnStartup(e);

            var bootstrapper = new Bootstrapper();

            bootstrapper.Run();

        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            //base.ConfigureModuleCatalog(moduleCatalog);
            moduleCatalog.AddModule<ModuleCoverModule>();
            moduleCatalog.AddModule<ModuleMotorModule>();
            moduleCatalog.AddModule<ModuleNetworkModule>();
            moduleCatalog.AddModule<ModuleTestLedModule>();
            // Log các module đã tìm thấy
            LogHelper.Debug("=== Modules Found ===");
            foreach (var module in moduleCatalog.Modules)
            {
                LogHelper.Debug($"Module: {module.ModuleName} - {module.State}");
            }
        }
    }

}
