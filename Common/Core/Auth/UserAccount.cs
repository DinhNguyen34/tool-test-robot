namespace Common.Core.Auth
{
    public class UserAccount
    {
        public string Username    { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt         { get; set; } = string.Empty;
        public UserRole Role       { get; set; } = UserRole.Viewer;
        public string DisplayName  { get; set; } = string.Empty;
    }
}
