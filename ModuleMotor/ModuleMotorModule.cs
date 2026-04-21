using ModuleMotor.Views;
using Prism.Modularity;
using ModuleMotor.ViewModels;

namespace ModuleMotor
{
    public class ModuleMotorModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(MotorView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<MotorView, MotorViewModel>();
        }
    }
}
