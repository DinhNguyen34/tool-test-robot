using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModuleNetwork.Views
{
    public partial class NetworkView : UserControl
    {
        public NetworkView()
        {
            InitializeComponent();
            LogDataGrid.PreviewMouseWheel += OnDataGridMouseWheel;
        }

        private void OnDataGridMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the DataGrid's internal ScrollViewer and scroll it directly
            var scrollViewer = GetChildOfType<ScrollViewer>(LogDataGrid);
            if (scrollViewer == null) return;

            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private static T? GetChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var result = GetChildOfType<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
