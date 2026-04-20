using Intel.RealSense;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Device selection
        private ObservableCollection<DeviceInfo> _availableDevices = new ObservableCollection<DeviceInfo>();
        private string _selectedDeviceSerial = "";

        // Advanced Controls
        private double _exposure = 1000;
        private double _gain = 16;

        // Recording
        private bool _isRecording = false;
        private string _recordingPath = "";

        // Advanced Controls
        private double _laserPower = 150;

        // Post-processing Filters
        private bool _isDecimationFilterEnabled = false;
        private bool _isSpatialFilterEnabled = true;
        private bool _isTemporalFilterEnabled = true;
        private bool _isHoleFillingFilterEnabled = true;
        private bool _isAlignEnabled = true;

        // Visualizing
        private double _minDistance = 0.1;
        private double _maxDistance = 4.0;
        private string _selectedColormap = "Jet";
        private bool _is3DMode = false;

        public string SelectedPreset { get => _selectedPreset; set => SetProperty(ref _selectedPreset, value); }
        public string SelectedResolution { get => _selectedResolution; set => SetProperty(ref _selectedResolution, value); }
        public int SelectedFPS { get => _selectedFPS; set => SetProperty(ref _selectedFPS, value); }

        public int GetWidth() => int.Parse(SelectedResolution.Split('x')[0].Trim());
        public int GetHeight() => int.Parse(SelectedResolution.Split('x')[1].Trim());

        public bool IsDepthEnabled { get => _isDepthEnabled; set => SetProperty(ref _isDepthEnabled, value); }
        public string SelectedDepthFormat { get => _selectedDepthFormat; set => SetProperty(ref _selectedDepthFormat, value); }
        public bool Infrared1Enabled { get => _infrared1Enabled; set => SetProperty(ref _infrared1Enabled, value); }
        public bool Infrared2Enabled { get => _infrared2Enabled; set => SetProperty(ref _infrared2Enabled, value); }
        public bool IsColorEnabled { get => _isColorEnabled; set => SetProperty(ref _isColorEnabled, value); }
        public string SelectedColorFormat { get => _selectedColorFormat; set => SetProperty(ref _selectedColorFormat, value); }
        
        // Corrected typo
        public bool AutoExposureEnabled { get => _autoExposureEnabled; set => SetProperty(ref _autoExposureEnabled, value); }
        public string AsicTemperature { get => _asicTemperature; set => SetProperty(ref _asicTemperature, value); }

        // Advanced Control Properties
        public double Exposure { get => _exposure; set => SetProperty(ref _exposure, value); }
        public double Gain { get => _gain; set => SetProperty(ref _gain, value); }
        public double LaserPower { get => _laserPower; set => SetProperty(ref _laserPower, value); }

        // Filter Properties
        public bool IsDecimationFilterEnabled { get => _isDecimationFilterEnabled; set => SetProperty(ref _isDecimationFilterEnabled, value); }
        public bool IsSpatialFilterEnabled { get => _isSpatialFilterEnabled; set => SetProperty(ref _isSpatialFilterEnabled, value); }
        public bool IsTemporalFilterEnabled { get => _isTemporalFilterEnabled; set => SetProperty(ref _isTemporalFilterEnabled, value); }
        public bool IsHoleFillingFilterEnabled { get => _isHoleFillingFilterEnabled; set => SetProperty(ref _isHoleFillingFilterEnabled, value); }
        public bool IsAlignEnabled { get => _isAlignEnabled; set => SetProperty(ref _isAlignEnabled, value); }

        // Visualization Properties
        public double MinDistance { get => _minDistance; set => SetProperty(ref _minDistance, value); }
        public double MaxDistance { get => _maxDistance; set => SetProperty(ref _maxDistance, value); }
        public string SelectedColormap { get => _selectedColormap; set => SetProperty(ref _selectedColormap, value); }
        public bool Is3DMode { get => _is3DMode; set => SetProperty(ref _is3DMode, value); }

        // Device selection properties
        public ObservableCollection<DeviceInfo> AvailableDevices { get => _availableDevices; set => SetProperty(ref _availableDevices, value); }
        public string SelectedDeviceSerial { get => _selectedDeviceSerial; set => SetProperty(ref _selectedDeviceSerial, value); }

        // Recording properties
        public bool IsRecording { get => _isRecording; set => SetProperty(ref _isRecording, value); }
        public string RecordingPath { get => _recordingPath; set => SetProperty(ref _recordingPath, value); }

        public void ResetToDefaults()
        {
            SelectedPreset = "Default";
            SelectedResolution = "848 x 480";
            SelectedFPS = 30;
            IsDepthEnabled = true;
            IsColorEnabled = true;
            AutoExposureEnabled = true;
            Exposure = 1000;
            Gain = 16;
            LaserPower = 150;
            IsDecimationFilterEnabled = false;
            IsSpatialFilterEnabled = true;
            IsTemporalFilterEnabled = true;
            IsHoleFillingFilterEnabled = true;
            IsAlignEnabled = true;
            MinDistance = 0.1;
            MaxDistance = 4.0;
            SelectedColormap = "Jet";
            Is3DMode = false;
        }

        public class DeviceInfo
        {
            public string Name { get; set; }
            public string Serial { get; set; }
            public string DisplayName => $"{Name} ({Serial})";
        }
    }
}
