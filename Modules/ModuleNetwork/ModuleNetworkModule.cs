using ModuleNetwork.ViewModels;
using ModuleNetwork.Views;
using Prism.Modularity;

namespace ModuleNetwork
{
    public class ModuleNetworkModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(NetworkView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<NetworkView, NetworkViewModel>();
        }
    }
}
