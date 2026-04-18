using Common.Core.Auth;

namespace RobotTesting.Auth
{
    public interface IAuthService
    {
        /// <summary>
        /// Returns the matching UserAccount when credentials are valid, null otherwise.
        /// </summary>
        Task<UserAccount?> ValidateAsync(string username, string password);
    }
}
