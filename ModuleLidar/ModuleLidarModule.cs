using ModuleLidar.ViewModels;
using ModuleLidar.Views;
using Prism.Modularity;

namespace ModuleLidar
{
    public class ModuleLidarModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(LidarView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<LidarView, LidarViewModel>();
        }
    }
}