using System;
using System.Collections.Generic;
using System.Text;
using Prism.Commands;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveCharts;
using LiveCharts.Wpf;

namespace ModuleLidar.ViewModels
{
    public class LidarViewModel : BindableBase
    {
        private readonly IRegionManager _regionManager;
        public DelegateCommand BackCommand { get; }
        public LidarViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
            BackCommand = new DelegateCommand(() =>
            {
                _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
            });
        }
    }
}
