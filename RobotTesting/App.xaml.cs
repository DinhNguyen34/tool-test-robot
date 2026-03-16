using Common.Core.Helpers;
using ModuleCover;
using Prism.Unity;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Windows;

namespace RobotTesting
{
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            PrismHelper.Init(Container);
            return Container.Resolve<MainWindow>();
        }
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {

        }
        protected override void OnStartup(StartupEventArgs e)
        {
            LogHelper.Init();

            this.DispatcherUnhandledException += (s, e)
                => MessageBox.Show("Unhandled exception occurred: \n" + e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            base.OnStartup(e);
        }

        protected override IModuleCatalog CreateModuleCatalog()
        {
            return new DirectoryModuleCatalog() { ModulePath = @".\Modules" };
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            //base.ConfigureModuleCatalog(moduleCatalog);
            moduleCatalog.AddModule<ModuleCoverModule>();
            // Log các module đã tìm thấy
            LogHelper.Debug("=== Modules Found ===");
            foreach (var module in moduleCatalog.Modules)
            {
                LogHelper.Debug($"Module: {module.ModuleName} - {module.State}");
            }
        }
    }

}
