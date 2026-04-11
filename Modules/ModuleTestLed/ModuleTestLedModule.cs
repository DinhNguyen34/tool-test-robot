using ModuleTestLed.Views;
using Prism.Modularity;

namespace ModuleTestLed
{
    public class ModuleTestLedModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(TestLedView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
        }
    }
}
