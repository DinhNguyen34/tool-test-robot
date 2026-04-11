using ModuleCamera.Views;
using Prism.Modularity;

namespace ModuleCamera
{
    public class ModuleCameraModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var regionManager = containerProvider.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("CoverRegion", typeof(CameraView));
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
        }
    }
}