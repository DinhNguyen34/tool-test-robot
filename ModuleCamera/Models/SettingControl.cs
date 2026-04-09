using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModuleCamera.Models
{
    public class SettingControl : BindableBase
    {

        // Khai báo các biến để lưu giá trị
        private string _selectedPreset = "Custom";
        private string _selectedResolution = "848 x 480";
        private int _selectedFPS = 30;
        private bool _isDepthEnabled = true;
        private string _selectedDepthFormat = "Z16";
        private bool _infrared1Enabled = false;
        private bool _infrared2Enabled = false;
        private bool _isColorEnabled = true;
        private string _selectedColorFormat = "RGB8";
        private bool _autoExposureEnabled = true;
        private string _asicTemperature = "0.0";

        public string SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    RaisePropertyChanged(nameof(SelectedPreset));
                }
            }
            //set => SetProperty(ref _selectedPreset, value);
        }

        
        public string SelectedResolution
        {
            get => _selectedResolution;
            set => SetProperty(ref _selectedResolution, value);
        }

        
        public int SelectedFPS
        {
            get => _selectedFPS;
            set => SetProperty(ref _selectedFPS, value);
        }

        // Get value Height and Width of resolution
        public int GetWidth() => int.Parse(SelectedResolution.Split('x')[0].Trim());
        public int GetHeight() => int.Parse(SelectedResolution.Split('x')[1].Trim());

        // CheckBox Depth
        public bool IsDepthEnabled
        {
            get => _isDepthEnabled;
            set 
            { 
                _isDepthEnabled = value; 
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsDepthEnabled))); 
            }
        }

        // ComboBox Color Format
        public string SelectedDepthFormat
        {
            get => _selectedDepthFormat;
            set
            {
                _selectedDepthFormat = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedDepthFormat)));
            }
        }

        // CheckBox Infrafed 1
        public bool Infrared1Enabled
        {
            get => _infrared1Enabled;
            set
            {
                _infrared1Enabled = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Infrared1Enabled)));
            }
        }


        // CheckBox Infrafed 2
        public bool Infrared2Enabled
        {
            get => _infrared2Enabled;
            set
            {
                _infrared2Enabled = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Infrared2Enabled)));
            }
        }

        public bool IsColorEnabled
        {
            get => _isColorEnabled;
            set
            {
                _isColorEnabled = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsColorEnabled)));
            }
        }

        // ComboBox Color Format
        public string SelectedColorFormat
        {
            get => _selectedColorFormat;
            set 
            {
                _selectedColorFormat= value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedColorFormat))); 
            }
        }

        // CheckBox Auto Exposure Enable
        public bool AutoExprosureEnabled
        {
            get => _autoExposureEnabled;
            set
            {
                _autoExposureEnabled = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AutoExprosureEnabled)));
            }
        }

        // Temputure of ASIC
        public string AsicTemperature
        {
            get => _asicTemperature;
            set 
            { 
                _asicTemperature = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(AsicTemperature)));
            }
        }

    }
}
