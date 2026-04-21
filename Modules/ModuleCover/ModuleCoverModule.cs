using ModuleCover.ViewModels;
using ModuleCover.Views;
using Prism.Ioc;
using Prism.Modularity;

namespace ModuleCover
{
    public class ModuleCoverModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            // Navigation is handled by Bootstrapper (LoginView first, then CoverRegion after login).
            // Do NOT call RegisterViewWithRegion here — it would push CoverRegion before login.
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<CoverRegion, CoverRegionViewModel>();
        }
    }
}
