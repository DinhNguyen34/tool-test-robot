using RobotTesting.ViewModels;
using System.Windows.Controls;

namespace RobotTesting.Views
{
    public partial class LoginView : UserControl
    {
        public LoginView()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
                vm.LoginCommand.Execute(PwdPassword.Password);
        }
    }
}
