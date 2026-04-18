using Common.Core.Auth;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using RobotTesting.Auth;

namespace RobotTesting.ViewModels
{
    public class LoginViewModel : BindableBase
    {
        private readonly IAuthService  _authService;
        private readonly IUserSession  _session;
        private readonly IRegionManager _regionManager;

        private string _username    = string.Empty;
        private string _errorMessage = string.Empty;
        private bool   _isBusy;

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                SetProperty(ref _errorMessage, value);
                RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        /// <summary>
        /// CommandParameter is the plain-text password supplied by the code-behind,
        /// because PasswordBox.Password is not a dependency property.
        /// </summary>
        public DelegateCommand<string> LoginCommand { get; }

        public LoginViewModel(
            IAuthService authService,
            IUserSession session,
            IRegionManager regionManager)
        {
            _authService   = authService;
            _session       = session;
            _regionManager = regionManager;

            LoginCommand = new DelegateCommand<string>(ExecuteLoginAsync);
        }

        private async void ExecuteLoginAsync(string password)
        {
            ErrorMessage = string.Empty;
            IsBusy = true;
            try
            {
                var account = await _authService.ValidateAsync(Username.Trim(), password);
                if (account is null)
                {
                    ErrorMessage = "Invalid username or password.";
                    return;
                }

                _session.Login(account);
                _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
