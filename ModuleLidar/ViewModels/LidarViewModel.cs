using Common.Core.Helpers;
using LiveCharts;
using LiveCharts.Wpf;
using ModuleLidar;
using ModuleLidar.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ModuleLidar.ViewModels
{
    public class LidarViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly DispatcherTimer _updateTimer;
        private ConcurrentQueue<Point3D> _pointBuffer = new ConcurrentQueue<Point3D>();
        private readonly Queue<(Point3D Point, DateTime Expiry)> _persistentPoints = new Queue<(Point3D, DateTime)>();
        private uint _lidarHandle = 0;
        private StreamWriter? _recordWriter = null;
        private bool isFocus = false;

        private readonly DecodedPointCloudCallback _pointCb;
        private readonly DecodedInternalInfoCallback _infoCb;
        private readonly DecodedImuDataCallback _imuCb;
        private readonly InfoChangeCallback _discoveryCb;

        public ObservableCollection<LidarInfoModel> DiscoveredLidars { get; } = new ObservableCollection<LidarInfoModel>();

        private LidarInfoModel? _selectedLidar;
        public LidarInfoModel? SelectedLidar
        {
            get => _selectedLidar;
            set
            {
                if (SetProperty(ref _selectedLidar, value) && value != null)
                {
                    _lidarHandle = value.Handle;
                    LidarInfo = value;
                }
            }
        }

        private LidarInfoModel _lidarInfo = new LidarInfoModel();
        public LidarInfoModel LidarInfo
        {
            get => _lidarInfo;
            set => SetProperty(ref _lidarInfo, value);
        }

        private CameraSettingsModel _cameraSettings = new CameraSettingsModel();
        public CameraSettingsModel CameraSettings
        {
            get => _cameraSettings;
            set => SetProperty(ref _cameraSettings, value);
        }

        private Point3DCollection _lidarPoints = new Point3DCollection();
        public Point3DCollection LidarPoints
        {
            get => _lidarPoints;
            set => SetProperty(ref _lidarPoints, value);
        }

        public DelegateCommand NormalModeCommand { get; }
        public DelegateCommand StandbyModeCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ToggleRecordCommand { get; }
        public DelegateCommand BackCommand { get; }

        public LidarViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;

            NormalModeCommand = new DelegateCommand(() => SetLidarMode(LivoxLidarWorkMode.SAMPLING));
            StandbyModeCommand = new DelegateCommand(() => SetLidarMode(LivoxLidarWorkMode.IDLE));
            ToggleRecordCommand = new DelegateCommand(ToggleRecording);

            BackCommand = new DelegateCommand(() =>
            {
                _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
            });

            _pointCb = OnDecodedPointCloud;
            _infoCb = OnDecodedInternalInfo;
            _imuCb = OnImuDataReceived;
            _discoveryCb = OnDeviceDiscovered;
            InitializeLidar();
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(100);
            _updateTimer.Tick += OnUpdateTimerTick;
        }

        private void InitializeLidar()
        {
            try
            {
                LivoxApi.DisableConsoleLogger();

                string configPath = Path.Combine(CommonApplicationUtilities.GetExeDirectory(), "config.json");
                if (LivoxApi.InitSdk(configPath))
                {
                    LivoxApi.SetDecodedPointCloudCallback(_pointCb, IntPtr.Zero);
                    LivoxApi.SetDecodedImuCallback(_imuCb, IntPtr.Zero);
                    LivoxApi.SetInfoChangeCallback(_discoveryCb, IntPtr.Zero);
                    LivoxApi.StartSdk();
                    LidarInfo.WorkMode = "SCANNING...";
                }
                else
                {
                    LidarInfo.WorkMode = "INIT FAILED";
                }
            }
            catch (Exception ex)
            {
                LidarInfo.WorkMode = "SDK ERROR";
                LogHelper.Exception(ex);
            }
        }

        private void OnDeviceDiscovered(uint handle, ref LivoxLidarInfo info, IntPtr client_data)
        {
            var device = info;
            Application.Current.Dispatcher.Invoke(() => {
                var existing = DiscoveredLidars.FirstOrDefault(x => x.SN == device.sn);
                if (existing == null)
                {
                    var newLidar = new LidarInfoModel { SN = device.sn, IP = device.lidar_ip, Handle = handle };
                    DiscoveredLidars.Add(newLidar);
                    if (SelectedLidar == null) SelectedLidar = newLidar;
                }
            });
        }

        private bool IsValidPoint(DecodedPoint p)
        {
            if (p.noise_type != 0) return false;
            if (p.reflectivity < 10) return false;

            float d2 = p.x * p.x + p.y * p.y + p.z * p.z;
            if (d2 < 0.0025f) return false; 
            if (d2 > 10000f) return false;    

            return true;
        }
        private void OnDecodedPointCloud(uint handle, uint dot_num, IntPtr data, IntPtr client_data)
        {
            if (!isFocus) return;
            if (handle != _lidarHandle && _lidarHandle == 0)
                _lidarHandle = handle;

            if (handle != _lidarHandle || data == IntPtr.Zero)
                return;

            int size = Marshal.SizeOf<DecodedPoint>();

            for (int i = 0; i < dot_num; i++)
            {
                IntPtr ptr = IntPtr.Add(data, i * size);
                var pt = Marshal.PtrToStructure<DecodedPoint>(ptr);

                if (float.IsNaN(pt.x) || float.IsInfinity(pt.x)) continue;

                if (!IsValidPoint(pt))  continue;

                var p3d = new Point3D(pt.x, pt.y, pt.z);
                _pointBuffer.Enqueue(p3d);

                if (LidarInfo.IsRecording && _recordWriter != null)
                {
                    _recordWriter.WriteLine($"{pt.x},{pt.y},{pt.z},{pt.reflectivity}");
                }
            }
        }
        private int ImuCount = 0;

        private void OnImuDataReceived(uint handle, ref DecodedImuPoint imu_data, IntPtr client_data)
        {
            if (!isFocus) return;
            if (handle != _lidarHandle) return;
            ImuCount++;
            if(ImuCount < 20) return;
            ImuCount = 0;
            var imu = imu_data;
            
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LidarInfo.IMUFrequency = 200;
                    LidarInfo.AccX = imu.acc_x;
                    LidarInfo.AccY = imu.acc_y;
                    LidarInfo.AccZ = imu.acc_z;

                    LidarInfo.Roll = Math.Atan2(imu.acc_y, imu.acc_z) * 180 / Math.PI;
                    LidarInfo.Pitch = Math.Atan2(-imu.acc_x, Math.Sqrt(imu.acc_y * imu.acc_y + imu.acc_z * imu.acc_z)) * 180 / Math.PI;
                });
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
        }

        private void OnDecodedInternalInfo(LivoxLidarStatus status, uint handle, ref DecodedDeviceStatus response, IntPtr client_data)
        {
            if (status == LivoxLidarStatus.Success && handle == _lidarHandle)
            {
                var info = response;
                Application.Current.Dispatcher.Invoke(() => {
                    LidarInfo.SN = info.sn;
                    LidarInfo.Temperature = info.core_temp / 100.0;
                    LidarInfo.WorkMode = ((LivoxLidarWorkMode)info.work_mode).ToString();
                    if(LidarInfo.WorkMode == LivoxLidarWorkMode.IDLE.ToString())
                    {
                        _pointBuffer = new ConcurrentQueue<Point3D>();
                    }
                });
            }
        }

        private void SetLidarMode(LivoxLidarWorkMode mode)
        {
            if (_lidarHandle == 0) return;
            LivoxApi.SetWorkMode(_lidarHandle, mode, (status, handle, response, data) => {
                if (status == LivoxLidarStatus.Success)
                {
                    LivoxApi.QueryDecodedInternalInfo(handle, _infoCb, IntPtr.Zero);
                }
            }, IntPtr.Zero);
        }

        private void ToggleRecording()
        {
            if (!LidarInfo.IsRecording)
            {
                try
                {
                    string fileName = $"lidar_data_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                    _recordWriter = new StreamWriter(fileName);
                    _recordWriter.WriteLine("X,Y,Z,Intensity");
                    LidarInfo.IsRecording = true;
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Recording failed: {ex.Message}");
                }
            }
            else
            {
                LidarInfo.IsRecording = false;
                _recordWriter?.Close();
                _recordWriter = null;
            }
        }

        private void OnUpdateTimerTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            DateTime expiry = now.AddMilliseconds(CameraSettings.FrameTime);

            int newPointsCount = 0;
            while (_pointBuffer.TryDequeue(out var pt) && newPointsCount < 50000)
            {
                _persistentPoints.Enqueue((pt, expiry));
                newPointsCount++;
            }

            while (_persistentPoints.Count > 0 && _persistentPoints.Peek().Expiry < now)
            {
                _persistentPoints.Dequeue();
            }
            
            var activePoints = new Point3DCollection();
            foreach (var item in _persistentPoints)
            {
                activePoints.Add(item.Point);
            }

            LidarPoints = activePoints;
        }

        private void ShutdownLidar()
        {
            _updateTimer.Stop();
            _recordWriter?.Close();
            LivoxApi.UninitSdk();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            
            _pointBuffer.Clear();
            _updateTimer.Start();
            isFocus = true;
            if (_lidarHandle != 0)
            {
                LivoxApi.QueryDecodedInternalInfo(_lidarHandle, _infoCb, IntPtr.Zero);
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            _updateTimer.Stop();
            _recordWriter?.Close();
            isFocus = false;
        }

        public void Dispose()
        {
            ShutdownLidar();
        }
    }
}
