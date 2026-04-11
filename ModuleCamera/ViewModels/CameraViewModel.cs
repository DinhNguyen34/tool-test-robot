using LiveCharts;
using LiveCharts.Wpf;
using ModuleCamera.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ModuleCamera.ViewModels
{
    public class CameraViewModel : BindableBase
    {
        private int _currentMaxScale = 8;
        private readonly IRegionManager _regionManager;
        private readonly CameraDevice _cameraDevice;
        private SeriesCollection _FramedropSeries;
        public DelegateCommand BackCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand ConnectCommand { get; }

        public SeriesCollection FrameDropUpdate
        {
            get => _FramedropSeries;
            set => SetProperty(ref _FramedropSeries, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _isCameraOn;
        public bool IsCameraOn
        {
            get => _isCameraOn;
            set
            {
                if (SetProperty(ref _isCameraOn, value))
                {
                    ConnectionStatus = _isCameraOn ? "CONNECTED" : "DISCONNECTED";
                    StatusColor = _isCameraOn ? "#4BFF4B" : "#FF4B4B";
                }
            }
        }

        private SettingControl _cameraSettings;
        public SettingControl CameraSettings
        {
            get => _cameraSettings;
            set => SetProperty(ref _cameraSettings, value);
        }

        public CameraViewModel(IRegionManager regionManager)
        {
            _cameraDevice = new CameraDevice();
            CameraSettings = new SettingControl(); // Init settings object
            CameraSettings.PropertyChanged += (s, e) =>
            {
                // Check for configuration changes
                if (e.PropertyName == nameof(SettingControl.SelectedResolution) ||
                    e.PropertyName == nameof(SettingControl.SelectedFPS) ||
                    e.PropertyName == nameof(SettingControl.SelectedPreset))
                {
                    // If IsCameraOn = true the camera will be start
                    if (IsCameraOn)
                    {
                        RestartCamera();
                    }
                }
            };

            InitChart();
            InitDepthScale();
            _cameraDevice.OnColorFrameArrived = (bmp) => ColorSource = bmp;
            _cameraDevice.OnDepthFrameArrived = (bmp) => DepthSource = bmp;
            _cameraDevice.OnRangeChanged = (range) => UpdateDepthScale(range);
            _cameraDevice.OnFrameDropsUpdate = (val) => UpdateFrameDrop(val);

            ConnectCommand = new DelegateCommand(() =>
            {
                if (IsCameraOn)
                {
                    _cameraDevice.Start(this.CameraSettings);
                    AddLog("Camera started", "Success");
                }
                else
                {
                    _cameraDevice.Stop();
                    AddLog("Camera stopped", "Success");
                }
            });

            ClearLogCommand = new DelegateCommand(() =>  // Clear log
            {
                LogEntries.Clear();
                ErrorCount = 0;
                WarningCount = 0;
                SuccessCount = 0;
            });

            _regionManager = regionManager;
            BackCommand = new DelegateCommand(() =>
            {
                _regionManager.RequestNavigate("CoverRegion", "CameraView");
            });
        }

        private void RestartCamera()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _cameraDevice.Stop();
                System.Threading.Thread.Sleep(1000);
                _cameraDevice.Start(this.CameraSettings);
                AddLog($"The configuration has changed: {CameraSettings.SelectedResolution} @ {CameraSettings.SelectedFPS} FPS", "Warning");
            });
        }

        private int _errorCount = 0;
        public int ErrorCount
        {
            get => _errorCount;
            set => SetProperty(ref _errorCount, value);
        }

        private int _warningCount = 0;
        public int WarningCount
        {
            get => _warningCount;
            set => SetProperty(ref _warningCount, value);
        }

        private int _successCount = 0;
        public int SuccessCount
        {
            get => _successCount;
            set => SetProperty(ref _successCount, value);
        }

        public void InitChart()
        {
            FrameDropUpdate = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Drops",
                    Values = new ChartValues<double>(new double[60]),
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#46A3FF")),
                    Fill = new SolidColorBrush(Color.FromArgb(40, 70, 163, 255)),
                    LineSmoothness = 0.5
                }
            };
        }

        public void UpdateFrameDrop(double value)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                if (FrameDropUpdate != null && FrameDropUpdate.Count > 0)
                {
                    var values = FrameDropUpdate[0].Values;
                    values.Add(value);
                    if (values.Count > 60) values.RemoveAt(0);
                }
            });
        }

        private BitmapSource _colorSource;
        public BitmapSource ColorSource
        {
            get => _colorSource;
            set => SetProperty(ref _colorSource, value);
        }

        private string _connectionStatus = "DISCONNECTED";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _statusColor = "#FF4B4B";
        public string StatusColor
        {
            get => _statusColor;
            set => SetProperty(ref _statusColor, value);
        }

        public ObservableCollection<LogItem> LogEntries { get; set; } = new ObservableCollection<LogItem>();

        public void AddLog(string message, string level)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, new LogItem { Time = DateTime.Now.ToString("HH:mm:ss"), Message = message, Type = level });
                if (level == "Error")
                {
                    ErrorCount++;
                }
                else if (level == "Warning")
                {
                    WarningCount++;
                }
                else
                {
                    SuccessCount++;
                }
            });
        }

        public class LogItem
        {
            public string Time { get; set; }
            public string Message { get; set; }
            public string Type { get; set; }
        }

        private string _mousePosText = "X: 0, Y: 0";
        public string MousePosText
        {
            get => _mousePosText;
            set => SetProperty(ref _mousePosText, value);
        }

        private string _distanceText = "0.000 m";
        public string DistanceText
        {
            get => _distanceText;
            set => SetProperty(ref _distanceText, value);
        }

        private double _tooltipX;
        public double TooltipX
        {
            get => _tooltipX;
            set => SetProperty(ref _tooltipX, value);
        }

        private double _tooltipY;
        public double TooltipY
        {
            get => _tooltipY;
            set => SetProperty(ref _tooltipY, value);
        }

        private Visibility _isTooltipVisible = Visibility.Collapsed;
        public Visibility IsTooltipVisible
        {
            get => _isTooltipVisible;
            set => SetProperty(ref _isTooltipVisible, value);
        }

        private BitmapSource _depthSource;
        public BitmapSource DepthSource
        {
            get => _depthSource;
            set => SetProperty(ref _depthSource, value);
        }

        private ObservableCollection<string> _depthScaleLabels = new ObservableCollection<string>();
        public ObservableCollection<string> DepthScaleLabels
        {
            get => _depthScaleLabels;
            set
            {
                _depthScaleLabels = value;
                OnPropertyChanged(nameof(DepthScaleLabels));
            }
        }

        private Brush _depthGradientSource;
        public Brush DepthGradientSource
        {
            get => _depthGradientSource;
            set => SetProperty(ref _depthGradientSource, value);
        }

        public void UpdateDepthScale(float maxRange)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => 
            {
                DepthScaleLabels.Clear();
                for (int i = (int)maxRange; i >= 0; i--)
                {
                    DepthScaleLabels.Add($"{i}m");
                }
                UpdateGradientBrush(maxRange);
            });
        }

        public void UpdateMousePosition(int x, int y)
        {
            if (!IsCameraOn || _cameraDevice == null)
                return;
            MousePosText = $"X: {x}, Y: {y}";
            float distance = _cameraDevice.GetDistance(x, y);
            if (distance > 0)
            {
                DistanceText = $"{distance:F3} m";
                IsTooltipVisible = Visibility.Visible;
            }
            else
            {
                DistanceText = "N/A";
                IsTooltipVisible = Visibility.Collapsed;
            }
        }

        private void UpdateGradientBrush(float maxRange)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0)
            };
            brush.GradientStops.Add(new GradientStop(Colors.Blue, 0.0));
            brush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.25));
            brush.GradientStops.Add(new GradientStop(Colors.Green, 0.5));
            brush.GradientStops.Add(new GradientStop(Colors.Yellow, 0.75));
            brush.GradientStops.Add(new GradientStop(Colors.Red, 1.0));

            DepthGradientSource = brush;
            OnPropertyChanged(nameof(DepthGradientSource));
        }

        private bool _is3DMode = false;
        public bool Is3DMode
        {
            get => _is3DMode;
            set { _is3DMode = value; OnPropertyChanged(nameof(Is3DMode)); }
        }

        public void InitDepthScale()
        {
            DepthScaleLabels = new ObservableCollection<string>();
            UpdateDepthScale(8);
        }

        public void AutoAdjustScale(float detectedMax)
        {
            if (detectedMax <= 0.1f || detectedMax > 15.0f) return;
            int newMax = (int)Math.Ceiling(detectedMax);

            if (newMax != _currentMaxScale)
            {
                _currentMaxScale = newMax;
                UpdateDepthScale(newMax);
            }
        }
    }
}