using MaterialDesignThemes.Wpf;
using RobotTesting.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RobotTesting.Views
{
    public partial class LoginView : UserControl
    {
        private bool _isPasswordVisible;
        private bool _isSyncingPassword;

        public LoginView()
        {
            InitializeComponent();
            Loaded += LoginView_Loaded;
            IsVisibleChanged += LoginView_IsVisibleChanged;
        }

        private void BtnLogin_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
                vm.LoginCommand.Execute(GetCurrentPassword());
        }

        private void PwdPassword_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!_isSyncingPassword && !_isPasswordVisible)
            {
                _isSyncingPassword = true;
                TxtPasswordVisible.Text = PwdPassword.Password;
                _isSyncingPassword = false;
            }

            UpdatePasswordPlaceholder();
        }

        private void TxtPasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSyncingPassword && _isPasswordVisible)
            {
                _isSyncingPassword = true;
                PwdPassword.Password = TxtPasswordVisible.Text;
                _isSyncingPassword = false;
            }

            UpdatePasswordPlaceholder();
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                TxtPasswordVisible.Text = PwdPassword.Password;
                TxtPasswordVisible.Visibility = Visibility.Visible;
                PwdPassword.Visibility = Visibility.Collapsed;
                IcoPasswordVisibility.Kind = PackIconKind.Eye;
                TxtPasswordVisible.Focus();
                TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
            }
            else
            {
                PwdPassword.Password = TxtPasswordVisible.Text;
                PwdPassword.Visibility = Visibility.Visible;
                TxtPasswordVisible.Visibility = Visibility.Collapsed;
                IcoPasswordVisibility.Kind = PackIconKind.EyeOff;
                PwdPassword.Focus();
            }

            UpdatePasswordPlaceholder();
        }

        private void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            ClearSensitiveInputs();
        }

        private void LoginView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                ClearSensitiveInputs();
            }
        }

        private string GetCurrentPassword()
        {
            return _isPasswordVisible ? TxtPasswordVisible.Text : PwdPassword.Password;
        }

        private void ClearSensitiveInputs()
        {
            _isSyncingPassword = true;
            PwdPassword.Password = string.Empty;
            TxtPasswordVisible.Text = string.Empty;
            _isSyncingPassword = false;

            _isPasswordVisible = false;
            PwdPassword.Visibility = Visibility.Visible;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            IcoPasswordVisibility.Kind = PackIconKind.EyeOff;

            TxtOtp.Text = string.Empty;
            UpdatePasswordPlaceholder();
        }

        private void UpdatePasswordPlaceholder()
        {
            TxtPasswordPlaceholder.Visibility = string.IsNullOrEmpty(GetCurrentPassword())
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
