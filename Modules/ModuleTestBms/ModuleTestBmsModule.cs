using ModuleTestBms.Views;
using Prism.Modularity;

namespace ModuleTestBms
{
    public class ModuleTestBmsModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(TestBmsView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<TestBmsView>();
        }
    }
}
