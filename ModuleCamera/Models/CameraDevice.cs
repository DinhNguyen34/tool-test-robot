using Common.Core.Helpers;
using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ModuleCamera.Models
{
    public class CameraDevice : IDisposable
    {
        private Pipeline _pipeline;
        private Config _cfg;
        private PipelineProfile _profile;

        public Action<BitmapSource> OnColorFrameArrived { get; set; }
        public Action<BitmapSource> OnDepthFrameArrived { get; set; }
        public Action<FrameSet> OnNewFrameArrived { get; set; }
        public Action<double> OnFrameDropsUpdate { get; set; }
        public event Action<string> OnTemperatureUpdated;
        public Action<float> OnCenterDistanceAvailable { get; set; }

        public DepthFrame CurrentDepthFrame { get; private set; }
        private readonly Colorizer _colorizer = new Colorizer();
        private bool _isRunning = false;
        private int _frameCount = 0;
        private DateTime _lastTime = DateTime.Now;
        private DateTime _last3DTime = DateTime.Now;
        private const int TARGET_FPS = 30;
        private Sensor _depthSensor;
        private Sensor _colorSensor;

        // Post-processing filters
        private readonly DecimationFilter _decimationFilter = new DecimationFilter();
        private readonly SpatialFilter _spatialFilter = new SpatialFilter();
        private readonly TemporalFilter _temporalFilter = new TemporalFilter();
        private readonly HoleFillingFilter _holeFillingFilter = new HoleFillingFilter();
        private Align _align;

        public void Start(SettingControl settings)
        {
            if (_isRunning) return;

            try
            {
                _pipeline = new Pipeline();
                _cfg = new Config();

                int width = settings.GetWidth();
                int height = settings.GetHeight();
                int fps = settings.SelectedFPS;

                if (settings.IsColorEnabled)
                {
                    if (Enum.TryParse(settings.SelectedColorFormat, true, out Format colorFormat))
                        _cfg.EnableStream(Stream.Color, width, height, colorFormat, fps);
                    else
                        _cfg.EnableStream(Stream.Color, width, height, Format.Rgb8, fps);
                }

                if (settings.IsDepthEnabled)
                {
                    if (Enum.TryParse(settings.SelectedDepthFormat, true, out Format depthFormat))
                        _cfg.EnableStream(Stream.Depth, width, height, depthFormat, fps);
                    else
                        _cfg.EnableStream(Stream.Depth, width, height, Format.Z16, fps);
                }

                if (settings.Infrared1Enabled) _cfg.EnableStream(Stream.Infrared, 1, width, height, Format.Y8, fps);
                if (settings.Infrared2Enabled) _cfg.EnableStream(Stream.Infrared, 2, width, height, Format.Y8, fps);

                if (!string.IsNullOrEmpty(settings.SelectedDeviceSerial))
                {
                    _cfg.EnableDevice(settings.SelectedDeviceSerial);
                }

                if (settings.IsRecording && !string.IsNullOrEmpty(settings.RecordingPath))
                {
                    _cfg.EnableRecordToFile(settings.RecordingPath);
                }

                _profile = _pipeline.Start(_cfg);

                _depthSensor = _profile.Device.QuerySensors().FirstOrDefault(s => s.Is(Extension.DepthSensor));
                _colorSensor = _profile.Device.QuerySensors().FirstOrDefault(s => s.Is(Extension.ColorSensor));

                ApplySettings(settings);

                if (settings.IsAlignEnabled)
                {
                    _align = new Align(Stream.Color);
                }

                _isRunning = true;
                _lastTime = DateTime.Now;

                Task.Run(() => RunPipeline(settings));
            }
            catch (Exception ex)
            {
                _isRunning = false;
                LogHelper.Error("The camera can't start: " + ex.Message);
            }
        }

        private void RunPipeline(SettingControl settings)
        {
            while (_isRunning)
            {
                try
                {
                    using (var frames = _pipeline.WaitForFrames())
                    {
                        UpdateFrameStatistics();

                        FrameSet processedFrames = frames.As<FrameSet>();

                        if (_align != null)
                        {
                            processedFrames = _align.Process(frames).As<FrameSet>();
                        }

                        using (var colorFrame = processedFrames.ColorFrame)
                        {
                            if (colorFrame != null)
                            {
                                var bitmap = ConvertFrameToBitmap(colorFrame);
                                OnColorFrameArrived?.Invoke(bitmap);
                            }
                        }

                        using (var depthFrame = processedFrames.DepthFrame)
                        {
                            if (depthFrame != null)
                            {
                                Frame filtered = depthFrame;

                                if (settings.IsDecimationFilterEnabled) filtered = _decimationFilter.Process(filtered);
                                if (settings.IsSpatialFilterEnabled) filtered = _spatialFilter.Process(filtered);
                                if (settings.IsTemporalFilterEnabled) filtered = _temporalFilter.Process(filtered);
                                if (settings.IsHoleFillingFilterEnabled) filtered = _holeFillingFilter.Process(filtered);

                                using (var finalDepth = filtered.As<DepthFrame>())
                                {
                                    var oldFrame = CurrentDepthFrame;
                                    CurrentDepthFrame = finalDepth.Clone().As<DepthFrame>();
                                    oldFrame?.Dispose();

                                    // Colorize for UI
                                    using (var colorizedDepth = _colorizer.Process<VideoFrame>(finalDepth))
                                    {
                                        var depthBitmap = ConvertFrameToBitmap(colorizedDepth);
                                        OnDepthFrameArrived?.Invoke(depthBitmap);
                                    }

                                    float centerDist = finalDepth.GetDistance(finalDepth.Width / 2, finalDepth.Height / 2);
                                    OnCenterDistanceAvailable?.Invoke(centerDist);
                                }

                                if (filtered != depthFrame) filtered.Dispose();
                            }
                        }

                        UpdateAsicTemperature(settings);

                        if (settings.Is3DMode && (DateTime.Now - _last3DTime).TotalMilliseconds > 100)
                        {
                            _last3DTime = DateTime.Now;
                            OnNewFrameArrived?.Invoke(processedFrames.Clone().As<FrameSet>());
                        }

                        if (processedFrames != frames) processedFrames.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error("Pipeline Loop Error: " + ex.Message);
                }
            }
        }

        private void ApplySettings(SettingControl settings)
        {
            if (_depthSensor != null)
            {
                // Visual Preset
                try
                {
                    float presetValue = 2; // Default
                    switch (settings.SelectedPreset.Replace(" ", ""))
                    {
                        case "Custom": presetValue = 1; break;
                        case "Default": presetValue = 2; break;
                        case "Hand": presetValue = 3; break;
                        case "HighAccuracy": presetValue = 4; break;
                        case "HighDensity": presetValue = 5; break;
                        case "MediumDensity": presetValue = 6; break;
                    }
                    _depthSensor.Options[Option.VisualPreset].Value = presetValue;
                }
                catch { }

                // Laser Power
                try { _depthSensor.Options[Option.LaserPower].Value = (float)settings.LaserPower; } catch { }
            }

            if (_colorSensor != null)
            {
                try { _colorSensor.Options[Option.EnableAutoExposure].Value = settings.AutoExposureEnabled ? 1 : 0; } catch { }

                if (!settings.AutoExposureEnabled)
                {
                    try { _colorSensor.Options[Option.Exposure].Value = (float)settings.Exposure; } catch { }
                }

                try { _colorSensor.Options[Option.Gain].Value = (float)settings.Gain; } catch { }
            }
        }

        private void UpdateAsicTemperature(SettingControl settings)
        {
            try
            {
                if (_depthSensor != null)
                {
                    float temp = _depthSensor.Options[Option.AsicTemperature].Value;
                    settings.AsicTemperature = temp.ToString("F1");
                    OnTemperatureUpdated?.Invoke(settings.AsicTemperature);
                }
            }
            catch { }
        }

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
                LogHelper.Error("Error stopping camera: " + ex.Message);
            }
            finally
            {
                _pipeline = null;
                _cfg?.Dispose();
                _cfg = null;
                _profile?.Dispose();
                _profile = null;
                CurrentDepthFrame?.Dispose();
                CurrentDepthFrame = null;
                _align?.Dispose();
                _align = null;
            }
        }

        public void HardwareReset()
        {
            try
            {
                if (_profile != null && _profile.Device != null)
                {
                    _profile.Device.HardwareReset();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("Hardware Reset Error: " + ex.Message);
            }
        }

        private BitmapSource ConvertFrameToBitmap(VideoFrame frame)
        {
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Rgb24, null, frame.Data, frame.Stride * frame.Height, frame.Stride);
            bitmap.Freeze();
            return bitmap;
        }

        public float GetDistance(int x, int y)
        {
            var frame = CurrentDepthFrame;
            if (frame == null) return 0;
            try
            {
                if (x < 0 || x >= frame.Width || y < 0 || y >= frame.Height)
                    return 0;
                return frame.GetDistance(x, y);
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

        public static List<SettingControl.DeviceInfo> GetAvailableDevices()
        {
            var results = new List<SettingControl.DeviceInfo>();
            try
            {
                using (var ctx = new Context())
                {
                    var devices = ctx.QueryDevices();
                    foreach (var dev in devices)
                    {
                        results.Add(new SettingControl.DeviceInfo
                        {
                            Name = dev.Info[CameraInfo.Name],
                            Serial = dev.Info[CameraInfo.SerialNumber]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error listing devices: " + ex.Message);
            }
            return results;
        }

        public void Dispose()
        {
            Stop();
            _colorizer?.Dispose();
            _decimationFilter?.Dispose();
            _spatialFilter?.Dispose();
            _temporalFilter?.Dispose();
            _holeFillingFilter?.Dispose();
        }
    }
}