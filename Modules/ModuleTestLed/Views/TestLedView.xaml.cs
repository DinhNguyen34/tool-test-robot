using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModuleTestLed.Views
{
    public partial class TestLedView : UserControl
    {
        public TestLedView()
        {
            InitializeComponent();
        }

        private void ComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (DataContext is ViewModels.TestLedViewModel vm)
                vm.RefreshCanCommand.Execute();
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.ScrollToEnd();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                Window.GetWindow(this)?.DragMove();
            }
        }
    }
}
