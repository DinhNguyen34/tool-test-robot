using Common.Core.Helpers;
using ModuleCover;
using ModuleMotor;
using ModuleNetwork;
using ModuleTestLed;
using ModuleTestBms;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Unity;
using System.Windows;
using Unity;
//using ModuleGraph;

namespace RobotTesting
{
    public class Bootstrapper : PrismBootstrapper
    {
        protected override DependencyObject CreateShell()
        {

            return Container.Resolve<MainWindow>();
        }
    
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
        }

        protected override IModuleCatalog CreateModuleCatalog()
        {
            return new ModuleCatalog();
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            //base.ConfigureModuleCatalog(moduleCatalog);
            moduleCatalog.AddModule<ModuleCoverModule>();
            moduleCatalog.AddModule<ModuleMotorModule>();
            moduleCatalog.AddModule<ModuleNetworkModule>();
            moduleCatalog.AddModule<ModuleTestLedModule>();
            moduleCatalog.AddModule<ModuleTestBmsModule>();
            // Log các module đã tìm thấy
            LogHelper.Debug("=== Modules Found ===");
            foreach (var module in moduleCatalog.Modules)
            {
                LogHelper.Debug($"Module: {module.ModuleName} - {module.State}");
            }
        }
    }
}
