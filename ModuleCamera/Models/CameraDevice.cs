using Intel.RealSense;
using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ModuleCamera.Models
{
    public class CameraDevice
    {
        private Pipeline _pipeline;
        private Config _cfg;
        public Action<BitmapSource> OnColorFrameArrived { get; set; }
        public Action<BitmapSource> OnDepthFrameArrived { get; set; }
        public Action<Intel.RealSense.Frame> OnNewFrameArrived { get; set; }
        public Action<float> OnRangeChanged { get; set; }
        public Action<double> OnFrameDropsUpdate { get; set; }
        public event Action<string> OnTemperatureUpdated;
        public Action<float> OnCenterDistanceAvailable { get; set; }

        public DepthFrame CurrentDepthFrame { get; private set; }
        private readonly Colorizer _colorizer = new Colorizer();
        private bool _isRunning = false;
        private int _frameCount = 0;
        private DateTime _lastTime = DateTime.Now;
        private const int TARGET_FPS = 30;
        private Sensor _tempSensor;
        public void Start(SettingControl settings)
        {
            if (_isRunning) return;

            try
            {
                _pipeline = new Pipeline();
                _cfg = new Config();

                int width = (settings != null) ? settings.GetWidth() : 640;
                int height = (settings != null) ? settings.GetHeight() : 480;
                int fps = (settings != null) ? settings.SelectedFPS : 30;

                //_cfg.EnableStream(Stream.Color, width, height, Format.Rgb8, fps);
                //_cfg.EnableStream(Stream.Depth, width, height, Format.Z16, fps);

                // 1. Cấu hình luồng Color (nếu được bật)
                if (settings.IsColorEnabled)
                {
                    // Chuyển đổi string từ UI (ví dụ "Rgb8") sang Enum Format của Intel
                    if (Enum.TryParse(settings.SelectedColorFormat, true, out Format colorFormat))
                    {
                        _cfg.EnableStream(Stream.Color, width, height, colorFormat, fps);
                    }
                    else
                    {
                        _cfg.EnableStream(Stream.Color, width, height, Format.Rgb8, fps);
                    }
                }

                // 2. Cấu hình luồng Depth (nếu được bật)
                if (settings.IsDepthEnabled)
                {
                    if (Enum.TryParse(settings.SelectedDepthFormat, true, out Format depthFormat))
                    {
                        _cfg.EnableStream(Stream.Depth, width, height, depthFormat, fps);
                    }
                    else
                    {
                        _cfg.EnableStream(Stream.Depth, width, height, Format.Z16, fps);
                    }
                }

                var profile = _pipeline.Start(_cfg);
                _tempSensor = profile.Device.QuerySensors().FirstOrDefault();

                var sensor = profile.Device.QuerySensors().FirstOrDefault();
                if (sensor != null && settings != null)
                {
                    if (string.IsNullOrEmpty(settings.SelectedPreset))
                    {
                        settings.SelectedPreset = "Custom";
                    }
                    string presetName = settings.SelectedPreset.Replace(" ", "");

                    bool hasPresetOption = false;
                    foreach (var opt in sensor.Options)
                    {
                        if (opt.Key == Option.VisualPreset)
                        {
                            hasPresetOption = true;
                            break;
                        }
                    }
                    if (hasPresetOption)
                    {
                        float presetValue = 1;
                        switch (presetName)
                        {
                            case "Custom": presetValue = 1; break;
                            case "Default": presetValue = 2; break;
                            case "Hand": presetValue = 3; break;
                            case "HighAccuracy": presetValue = 4; break;
                            case "HighDensity": presetValue = 5; break;
                            case "MediumDensity": presetValue = 6; break;
                            default: presetValue = 1; break;
                        }
                        sensor.Options[Option.VisualPreset].Value = presetValue;
                    }
                }

                _isRunning = true;
                _lastTime = DateTime.Now;
                Task.Run(() => {
                    while (_isRunning)
                    {
                        try
                        {
                            using (var frames = _pipeline.WaitForFrames())
                            {
                                UpdateFrameStatistics();
                                // Color Sream
                                using (var colorFrame = frames.ColorFrame)
                                {
                                    if (colorFrame != null)
                                    {
                                        var bitmap = ConvertFrameToBitmap(colorFrame);
                                        OnColorFrameArrived?.Invoke(bitmap);
                                    }
                                }
                                // Depth Stream 2D
                                using (var depthFrame = frames.DepthFrame)
                                {
                                    if (depthFrame != null)
                                    {
                                        // GetDistance of pixel
                                        var oldFrame = CurrentDepthFrame;
                                        CurrentDepthFrame = depthFrame.Clone().As<DepthFrame>();
                                        oldFrame?.Dispose();

                                        // Create colorized images from depth data for display on the UI.
                                        using (var colorizedDepth = _colorizer.Process<VideoFrame>(depthFrame))
                                        {
                                            var depthBitmap = ConvertFrameToBitmap(colorizedDepth);
                                            OnDepthFrameArrived?.Invoke(depthBitmap);
                                        }

                                        float centerDist = depthFrame.GetDistance(depthFrame.Width / 2, depthFrame.Height / 2);
                                        MaxDistanceFound = centerDist;
                                        OnCenterDistanceAvailable?.Invoke(centerDist);
                                    }
                                }

                                // Get value of temperture
                                var tempOption = _tempSensor.Options[Option.AsicTemperature];
                                if (_tempSensor != null && tempOption != null)
                                {
                                    float temp = tempOption.Value;

                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        if (settings != null)
                                        {
                                            settings.AsicTemperature = temp.ToString("F1");
                                        }
                                        OnTemperatureUpdated?.Invoke(temp.ToString("F1"));
                                    });
                                }

                                OnNewFrameArrived?.Invoke(frames.Clone());
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("Pipeline Loop Error: " + ex.Message);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _isRunning = false;
                throw new Exception("The camera can't start." + ex.Message);
            }
        }

        public float MaxDistanceFound { get; private set; }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_pipeline != null)
                {
                    _pipeline.Stop();
                    _pipeline.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi khi dừng camera: " + ex.Message);
            }
            finally
            {
                _pipeline = null;
                _cfg?.Dispose();
                _cfg = null;
                CurrentDepthFrame?.Dispose();
                CurrentDepthFrame = null;
            }
        }

        // Tối ưu hóa chuyển đổi để dùng PixelFormats.Rgb24 cho Intel RealSense
        private BitmapSource ConvertFrameToBitmap(VideoFrame frame)
        {
            var bitmap = BitmapSource.Create(frame.Width, frame.Height,96, 96,PixelFormats.Rgb24, null,frame.Data,frame.Stride * frame.Height,frame.Stride);
            bitmap.Freeze();
            return bitmap;
        }

        public float GetDistance(int x, int y)
        {
            if (CurrentDepthFrame == null) return 0;
            try
            {
                // Bảo vệ giới hạn tọa độ tránh crash ứng dụng
                if (x < 0 || x >= CurrentDepthFrame.Width || y < 0 || y >= CurrentDepthFrame.Height)
                    return 0;

                return CurrentDepthFrame.GetDistance(x, y);
            }
            catch { return 0; }
        }
        private void UpdateFrameStatistics()
        {
            _frameCount++;
            var now = DateTime.Now;
            var interval = (now - _lastTime).TotalSeconds;
            if (interval >= 0.5)
            {
                double expectedFrames = TARGET_FPS * interval;
                double drops = Math.Max(0, expectedFrames - _frameCount);
                OnFrameDropsUpdate?.Invoke(drops);
                _frameCount = 0;
                _lastTime = now;
            }
        }
    }
}