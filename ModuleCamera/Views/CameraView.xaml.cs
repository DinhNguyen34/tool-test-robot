using ModuleCamera.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModuleCamera.Views
{
    public partial class CameraView : UserControl
    {
        public CameraView()
        {
            InitializeComponent();
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
            if (DataContext is CameraViewModel vm)
            {
                vm.IsTooltipVisible = Visibility.Collapsed;
            }
        }
    }
}