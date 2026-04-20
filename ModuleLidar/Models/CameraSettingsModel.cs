using Prism.Mvvm;

namespace ModuleLidar.Models
{
    public class CameraSettingsModel : BindableBase
    {
        private double _pointSize = 2.0;
        public double PointSize
        {
            get => _pointSize;
            set => SetProperty(ref _pointSize, value);
        }

        private string _colorMode = "Reflectivity";
        public string ColorMode
        {
            get => _colorMode;
            set => SetProperty(ref _colorMode, value);
        }

        private int _frameTime = 100;
        public int FrameTime
        {
            get => _frameTime;
            set => SetProperty(ref _frameTime, value);
        }
    }
}
