
using ModuleCover.Views;
using Prism.Modularity;
using System.Reflection;

namespace ModuleCover
    
{
    public class ModuleCoverModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(CoverRegion));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {

        }
    }

}
