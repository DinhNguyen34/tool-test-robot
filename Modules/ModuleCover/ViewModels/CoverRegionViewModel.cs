using Common.Core;
using Common.Core.Helpers;
using ModuleCover.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace ModuleCover.ViewModels
{
   
    public class CoverRegionViewModel : BindableBase
    {
        public CoverModel Model { get; } = new CoverModel();
        private readonly IRegionManager _regionManager;
        public DelegateCommand<object> OnCommandBtn => new DelegateCommand<object>(_onCommandBtn);

        public CoverRegionViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        #region Command
        private void _onCommandBtn(object obj)
        {
            try
            {
                switch ((TileType)obj)
                {
                    case TileType.Mortor:
                        _regionManager.RequestNavigate("CoverRegion", "MultiflashView");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
        }
        #endregion
    }
}
