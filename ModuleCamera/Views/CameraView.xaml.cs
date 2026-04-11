using Intel.RealSense;
using ModuleCamera.Models;
using ModuleCamera.Models;
using ModuleCamera.ViewModels;
using ModuleCamera.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input;
using System.Windows.Media;
namespace ModuleCamera.Views
{
    public partial class CameraView : UserControl
    {
        private CameraDevice _camera;
        public CameraView()
        {
            InitializeComponent();
            var vm = new CameraViewModel(null);
            this.DataContext = vm;
            _camera = new CameraDevice();

            _camera.OnColorFrameArrived += (bitmap) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var vm = this.DataContext as CameraViewModel;
                    if (vm != null)
                    {
                        vm.ColorSource = bitmap;
                    }
                });
            };
            btnCameraPower.Checked += (s, e) => StartCameraProcess();
            btnCameraPower.Unchecked += (s, e) => StopCameraProcess();

            _camera.OnCenterDistanceAvailable = (dist) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this.DataContext is CameraViewModel vm)
                    {
                        vm.AutoAdjustScale(dist);
                    }
                }));
            };
        }

        private void StartCameraProcess()
        {
            var vm = this.DataContext as CameraViewModel;
            if (vm == null) return;
            try
            {
                _camera.Start(vm.CameraSettings);

                vm.ConnectionStatus = "CONNECTED";
                vm.StatusColor = "#28A745";
                vm.AddLog("Camera Intel RealSense D405: ON", "Success");
            }
            catch (Exception ex)
            {
                vm.AddLog("Error: " + ex.Message, "Error");
                btnCameraPower.IsChecked = false;
            }
        }

        private void StopCameraProcess()
        {
            _camera.Stop();

            var vm = this.DataContext as CameraViewModel;
            if (vm != null)
            {
                vm.ColorSource = null;
                vm.ConnectionStatus = "DISCONNECTED";
                vm.StatusColor = "#FF4B4B";
                vm.AddLog("Camera Intel RealSense D405: OFF", "Warning");
            }
        }

        private void Img_MouseMove(object sender, MouseEventArgs e)
        {
            var img = sender as Image;
            if (img == null || img.Source == null) return;

            Point pos = e.GetPosition(img);

            double actualX = pos.X * (img.Source.Width / img.ActualWidth);
            double actualY = pos.Y * (img.Source.Height / img.ActualHeight);

            if (DataContext is CameraViewModel vm)
            {
                vm.UpdateMousePosition((int)actualX, (int)actualY);
                vm.TooltipX = pos.X + 15;
                vm.TooltipY = pos.Y + 15;
            }
        }

        private void Img_MouseLeave(object sender, MouseEventArgs e)
        {
            if (this.DataContext is CameraViewModel vm)
                vm.IsTooltipVisible = Visibility.Collapsed;
        }
    }
}