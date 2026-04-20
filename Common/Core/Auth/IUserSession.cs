namespace Common.Core.Auth
{
    public interface IUserSession
    {
        UserAccount? CurrentUser { get; }
        bool         IsAuthenticated { get; }
        UserRole     Role { get; }

        bool HasPermission(Permission permission);

        /// <summary>Stores the authenticated user and fires SessionChanged.</summary>
        void Login(UserAccount user);

        /// <summary>Clears the current user and fires SessionChanged.</summary>
        void Logout();

        /// <summary>Fires after Login() or Logout().</summary>
        event EventHandler? SessionChanged;
    }
}
