using Common.Core.Auth;
using Common.Core.Helpers;
using ModuleCamera;
using ModuleCover;
using ModuleImu;
using ModuleLidar;
using ModuleMotor;
using ModuleNetwork;
using ModuleTestLed;
using ModuleTestBms;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Navigation.Regions;
using Prism.Unity;
using RobotTesting.Auth;
using RobotTesting.ViewModels;
using RobotTesting.Views;
using System.Windows;
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
            containerRegistry.RegisterSingleton<IUserSession, UserSession>();
            containerRegistry.RegisterSingleton<IAuthService, FileAuthService>();
            containerRegistry.RegisterForNavigation<LoginView, LoginViewModel>("LoginView");
        }

        protected override IModuleCatalog CreateModuleCatalog()
        {
            return new ModuleCatalog();
        }

        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            moduleCatalog.AddModule<ModuleCoverModule>();
            moduleCatalog.AddModule<ModuleMotorModule>();
            moduleCatalog.AddModule<ModuleNetworkModule>();
            moduleCatalog.AddModule<ModuleTestLedModule>();
            moduleCatalog.AddModule<ModuleLidarModule>();
            moduleCatalog.AddModule<ModuleImuModule>();
            moduleCatalog.AddModule<ModuleCameraModule>();
            moduleCatalog.AddModule<ModuleTestBmsModule>();
            LogHelper.Debug("=== Modules Found ===");
            foreach (var module in moduleCatalog.Modules)
            {
                LogHelper.Debug($"Module: {module.ModuleName} - {module.State}");
            }
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RequestNavigate("CoverRegion", "LoginView");
        }
    }
}
