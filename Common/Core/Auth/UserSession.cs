namespace Common.Core.Auth
{
    public sealed class UserSession : IUserSession
    {
        // Permission sets per role
        private static readonly IReadOnlySet<Permission> AdminPermissions = new HashSet<Permission>
        {
            Permission.NavigateMotor,
            Permission.NavigateNetwork,
            Permission.NavigateLed,
            Permission.NavigateBms,
            Permission.NavigateCamera,
            Permission.NavigateLidar,
            Permission.ManageUsers
        };

        private static readonly IReadOnlySet<Permission> OperatorPermissions = new HashSet<Permission>
        {
            Permission.NavigateMotor,
            Permission.NavigateNetwork,
            Permission.NavigateLed,
            Permission.NavigateBms,
            Permission.NavigateCamera,
            Permission.NavigateLidar
        };

        private static readonly IReadOnlySet<Permission> ViewerPermissions = new HashSet<Permission>
        {
            Permission.NavigateLed,
            Permission.NavigateBms
        };

        public UserAccount? CurrentUser { get; private set; }

        public bool IsAuthenticated => CurrentUser is not null;

        public UserRole Role => CurrentUser?.Role ?? UserRole.Viewer;

        public event EventHandler? SessionChanged;

        public bool HasPermission(Permission permission)
        {
            if (!IsAuthenticated) return false;
            return Role switch
            {
                UserRole.Admin    => AdminPermissions.Contains(permission),
                UserRole.Operator => OperatorPermissions.Contains(permission),
                UserRole.Viewer   => ViewerPermissions.Contains(permission),
                _                 => false
            };
        }

        public void Login(UserAccount user)
        {
            CurrentUser = user;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Logout()
        {
            CurrentUser = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
