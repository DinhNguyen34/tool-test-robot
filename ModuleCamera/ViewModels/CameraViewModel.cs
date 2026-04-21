using Intel.RealSense;
using Microsoft.Win32;
using LiveCharts;
using LiveCharts.Wpf;
using ModuleCamera.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.IO;
using Intel.RealSense.Math;

namespace ModuleCamera.ViewModels
{
    public class CameraViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly CameraDevice _cameraDevice;
        private SeriesCollection _framedropSeries;
        private bool _isCameraOn;
        private SettingControl _cameraSettings;
        private BitmapSource _colorSource;
        private BitmapSource _depthSource;
        private string _connectionStatus = "DISCONNECTED";
        private string _statusColor = "#FF4B4B";
        private string _mousePosText = "X: 0, Y: 0";
        private string _distanceText = "0.000 m";
        private double _tooltipX;
        private double _tooltipY;
        private Visibility _isTooltipVisible = Visibility.Collapsed;
        private ObservableCollection<string> _depthScaleLabels = new ObservableCollection<string>();
        private Brush _depthGradientSource;
        private Point3DCollection _pointCloudPoints = new Point3DCollection();
        private bool _isProcessing3D = false;

        public DelegateCommand BackCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand ConnectCommand { get; }
        public DelegateCommand SnapshotCommand { get; }
        public DelegateCommand RecordCommand { get; }
        public DelegateCommand RefreshDevicesCommand { get; }
        public DelegateCommand FactoryResetCommand { get; }

        public SeriesCollection FrameDropUpdate { get => _framedropSeries; set => SetProperty(ref _framedropSeries, value); }
        public bool IsCameraOn { get => _isCameraOn; set => SetProperty(ref _isCameraOn, value); }
        public SettingControl CameraSettings { get => _cameraSettings; set => SetProperty(ref _cameraSettings, value); }
        public BitmapSource ColorSource { get => _colorSource; set => SetProperty(ref _colorSource, value); }
        public BitmapSource DepthSource { get => _depthSource; set => SetProperty(ref _depthSource, value); }
        public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
        public string StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }
        public string MousePosText { get => _mousePosText; set => SetProperty(ref _mousePosText, value); }
        public string DistanceText { get => _distanceText; set => SetProperty(ref _distanceText, value); }
        public double TooltipX { get => _tooltipX; set => SetProperty(ref _tooltipX, value); }
        public double TooltipY { get => _tooltipY; set => SetProperty(ref _tooltipY, value); }
        public Visibility IsTooltipVisible { get => _isTooltipVisible; set => SetProperty(ref _isTooltipVisible, value); }
        public ObservableCollection<string> DepthScaleLabels { get => _depthScaleLabels; set => SetProperty(ref _depthScaleLabels, value); }
        public Brush DepthGradientSource { get => _depthGradientSource; set => SetProperty(ref _depthGradientSource, value); }
        
        public bool Is3DMode 
        { 
            get => CameraSettings.Is3DMode; 
            set 
            { 
                if (CameraSettings.Is3DMode != value) 
                { 
                    CameraSettings.Is3DMode = value; 
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Is3DMode))); 
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Is3DViewVisible))); 
                } 
            } 
        }

        public Visibility Is3DViewVisible => Is3DMode ? Visibility.Visible : Visibility.Collapsed;
        public Point3DCollection PointCloudPoints { get => _pointCloudPoints; set => SetProperty(ref _pointCloudPoints, value); }

        public ObservableCollection<LogItem> LogEntries { get; } = new ObservableCollection<LogItem>();
        private int _errorCount, _warningCount, _successCount;
        public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }
        public int WarningCount { get => _warningCount; set => SetProperty(ref _warningCount, value); }
        public int SuccessCount { get => _successCount; set => SetProperty(ref _successCount, value); }

        private readonly PointCloud _pc = new PointCloud();

        public CameraViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
            _cameraDevice = new CameraDevice();
            CameraSettings = new SettingControl();
            
            CameraSettings.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(SettingControl.SelectedResolution) ||
                    e.PropertyName == nameof(SettingControl.SelectedFPS) ||
                    e.PropertyName == nameof(SettingControl.SelectedPreset))
                {
                    if (IsCameraOn) await RestartCamera();
                }
            };

            InitChart();
            InitDepthScale();

            _cameraDevice.OnColorFrameArrived = (bmp) => ColorSource = bmp;
            _cameraDevice.OnDepthFrameArrived = (bmp) => DepthSource = bmp;
            _cameraDevice.OnFrameDropsUpdate = UpdateFrameDrop;
            _cameraDevice.OnCenterDistanceAvailable = AutoAdjustScale;
            _cameraDevice.OnNewFrameArrived = Process3DData;

            RefreshDevicesCommand = new DelegateCommand(RefreshDevices);
            RefreshDevices(); 

            RecordCommand = new DelegateCommand(ToggleRecording);

            FactoryResetCommand = new DelegateCommand(async () =>
            {
                var result = System.Windows.MessageBox.Show("Are you sure you want to reset camera to factory defaults? This will reboot the device.",
                    "Factory Reset", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    AddLog("Performing Factory Reset...", "Warning");

                    CameraSettings.ResetToDefaults();

                    _cameraDevice.HardwareReset();

                    IsCameraOn = false;
                    ConnectionStatus = "REBOOTING";
                    StatusColor = "#FFFF4B";

                    await Task.Delay(3000);

                    AddLog("Camera rebooted. Restarting...", "Info");
                    await RestartCamera();
                }
            });

            ConnectCommand = new DelegateCommand(() =>
            {
                if (IsCameraOn)
                {
                    _cameraDevice.Start(CameraSettings);
                    ConnectionStatus = "CONNECTED";
                    StatusColor = "#4BFF4B";
                    AddLog("Camera started successfully", "Success");
                }
                else
                {
                    _cameraDevice.Stop();
                    ConnectionStatus = "DISCONNECTED";
                    StatusColor = "#FF4B4B";
                    
                    AddLog("Camera stopped", "Warning");
                }
            });

            ClearLogCommand = new DelegateCommand(() => {
                LogEntries.Clear();
                ErrorCount = WarningCount = SuccessCount = 0;
            });

            SnapshotCommand = new DelegateCommand(TakeSnapshot);

            BackCommand = new DelegateCommand(() => {
                _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
            });
        }

        private async Task RestartCamera()
        {
            _cameraDevice.Stop();
            await Task.Delay(1000);
            if (IsCameraOn) _cameraDevice.Start(CameraSettings);
            AddLog($"Configuration updated: {CameraSettings.SelectedResolution} @ {CameraSettings.SelectedFPS} FPS", "Warning");
        }

        private async void ToggleRecording()
        {
            if (CameraSettings.IsRecording)
            {
                // Stop recording
                CameraSettings.IsRecording = false;
                CameraSettings.RecordingPath = "";
                AddLog("Stopping recording and restarting stream...", "Warning");
                if (IsCameraOn) await RestartCamera();
                AddLog("Recording saved.", "Success");
            }
            else
            {
                // Start recording
                var sfd = new SaveFileDialog
                {
                    Filter = "Realsense Bag files (*.bag)|*.bag",
                    DefaultExt = ".bag",
                    FileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.bag"
                };

                if (sfd.ShowDialog() == true)
                {
                    CameraSettings.RecordingPath = sfd.FileName;
                    CameraSettings.IsRecording = true;
                    AddLog($"Starting recording to: {System.IO.Path.GetFileName(sfd.FileName)}", "Warning");
                    if (IsCameraOn) await RestartCamera();
                }
            }
        }

        private void RefreshDevices()
        {
            var devices = CameraDevice.GetAvailableDevices();
            CameraSettings.AvailableDevices.Clear();
            foreach (var d in devices) CameraSettings.AvailableDevices.Add(d);

            if (CameraSettings.AvailableDevices.Count > 0 && string.IsNullOrEmpty(CameraSettings.SelectedDeviceSerial))
            {
                CameraSettings.SelectedDeviceSerial = CameraSettings.AvailableDevices[0].Serial;
            }
            AddLog($"Found {devices.Count} RealSense devices", "Success");
        }

        private void Process3DData(FrameSet frames)
        {
            if (!Is3DMode || _isProcessing3D) 
            {
                frames.Dispose();
                return;
            }

            _isProcessing3D = true;
            Task.Run(() =>
            {
                try
                {
                    using (frames)
                    using (var depth = frames.DepthFrame)
                    {
                        if (depth == null) return;

                        using (var points = _pc.Calculate(depth))
                        {
                            var vertices = new Vertex[points.Count];
                            points.CopyVertices(vertices);

                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var point3DCollection = new Point3DCollection();
                                // Subsample for performance
                                int total = vertices.Length;
                                int step = total > 800000 ? 30 : (total > 400000 ? 15 : (total > 100000 ? 8 : 4));

                                for (int i = 0; i < total; i += step)
                                {
                                    var v = vertices[i];
                                    if (v.z > 0.1 && v.z < 8) // Valid depth range for D400 series
                                        point3DCollection.Add(new Point3D(v.x, -v.y, -v.z));
                                }
                                PointCloudPoints = point3DCollection;
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("3D Processing Error: " + ex.Message);
                }
                finally
                {
                    _isProcessing3D = false;
                }
            });
        }

        private void TakeSnapshot()
        {
            if (ColorSource == null) return;

            var saveFileDialog = new SaveFileDialog { Filter = "PNG Image|*.png|JPEG Image|*.jpg", FileName = $"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}" };
            if (saveFileDialog.ShowDialog() == true)
            {
                using (var fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    BitmapEncoder encoder = saveFileDialog.FilterIndex == 1 ? new PngBitmapEncoder() : new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(ColorSource));
                    encoder.Save(fileStream);
                }
                AddLog($"Snapshot saved to {Path.GetFileName(saveFileDialog.FileName)}", "Success");
            }
        }

        public void InitChart()
        {
            FrameDropUpdate = new SeriesCollection {
                new LineSeries {
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
            Application.Current?.Dispatcher.Invoke(() => {
                if (FrameDropUpdate != null && FrameDropUpdate.Count > 0) {
                    var values = FrameDropUpdate[0].Values;
                    values.Add(value);
                    if (values.Count > 60) values.RemoveAt(0);
                }
            });
        }

        public void AddLog(string message, string level)
        {
            Application.Current?.Dispatcher.Invoke(() => {
                LogEntries.Insert(0, new LogItem { Time = DateTime.Now.ToString("HH:mm:ss"), Message = message, Type = level });
                if (level == "Error") ErrorCount++;
                else if (level == "Warning") WarningCount++;
                else SuccessCount++;
            });
        }

        public void UpdateMousePosition(int x, int y)
        {
            if (!IsCameraOn || _cameraDevice == null) return;
            MousePosText = $"X: {x}, Y: {y}";
            float distance = _cameraDevice.GetDistance(x, y);
            if (distance > 0) {
                DistanceText = $"{distance:F3} m";
                IsTooltipVisible = Visibility.Visible;
            } else {
                DistanceText = "N/A";
                IsTooltipVisible = Visibility.Collapsed;
            }
        }

        public void InitDepthScale() { UpdateDepthScale(8); }

        public void UpdateDepthScale(float maxRange)
        {
            Application.Current?.Dispatcher.Invoke(() => {
                DepthScaleLabels.Clear();
                for (int i = (int)maxRange; i >= 0; i--) DepthScaleLabels.Add($"{i}m");
                UpdateGradientBrush();
            });
        }

        private void UpdateGradientBrush()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            brush.GradientStops.Add(new GradientStop(Colors.Blue, 0.0));
            brush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.25));
            brush.GradientStops.Add(new GradientStop(Colors.Green, 0.5));
            brush.GradientStops.Add(new GradientStop(Colors.Yellow, 0.75));
            brush.GradientStops.Add(new GradientStop(Colors.Red, 1.0));
            DepthGradientSource = brush;
        }

        public void AutoAdjustScale(float detectedMax)
        {
            if (detectedMax <= 0.1f || detectedMax > 15.0f) return;
            int newMax = (int)Math.Ceiling(detectedMax);
            if (newMax != DepthScaleLabels.Count - 1) UpdateDepthScale(newMax);
        }

        public void Dispose()
        {
            _cameraDevice.Dispose();
            _pc.Dispose();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _cameraDevice.Stop();
            ConnectionStatus = "DISCONNECTED";
            StatusColor = "#FF4B4B";
            AddLog("Camera stopped", "Warning");
            IsCameraOn = false;
        }

        public class LogItem
        {
            public string Time { get; set; }
            public string Message { get; set; }
            public string Type { get; set; }
        }
    }
}
