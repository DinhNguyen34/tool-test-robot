using Common.Core.Auth;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobotTesting.Auth
{
    /// <summary>
    /// Persists user accounts as JSON in %AppData%\RobotTesting\users.json.
    /// Passwords are stored as SHA-256(salt + password), hex-encoded.
    /// </summary>
    public sealed class FileAuthService : IAuthService
    {
        private static readonly string DataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RobotTesting",
            "users.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        private List<UserAccount>? _cache;

        public async Task<UserAccount?> ValidateAsync(string username, string password)
        {
            var accounts = await LoadAccountsAsync();
            var account  = accounts.FirstOrDefault(a =>
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

            if (account is null) return null;

            string hash = ComputeHash(account.Salt, password);
            return hash == account.PasswordHash ? account : null;
        }

        // ── private helpers ──────────────────────────────────────────────────

        private async Task<List<UserAccount>> LoadAccountsAsync()
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(DataPath))
            {
                _cache = BuildDefaultAccounts();
                await SaveAsync(_cache);
                return _cache;
            }

            string json = await File.ReadAllTextAsync(DataPath);
            _cache = JsonSerializer.Deserialize<List<UserAccount>>(json, JsonOpts)
                     ?? BuildDefaultAccounts();
            return _cache;
        }

        private async Task SaveAsync(List<UserAccount> accounts)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            string json = JsonSerializer.Serialize(accounts, JsonOpts);
            await File.WriteAllTextAsync(DataPath, json);
        }

        private static List<UserAccount> BuildDefaultAccounts()
        {
            return
            [
                CreateAccount("admin",    "admin123",    UserRole.Admin,    "Administrator"),
                CreateAccount("operator", "operator123", UserRole.Operator, "Operator"),
                CreateAccount("viewer",   "viewer123",   UserRole.Viewer,   "Viewer")
            ];
        }

        private static UserAccount CreateAccount(
            string username, string password, UserRole role, string displayName)
        {
            string salt = GenerateSalt();
            return new UserAccount
            {
                Username     = username,
                Salt         = salt,
                PasswordHash = ComputeHash(salt, password),
                Role         = role,
                DisplayName  = displayName
            };
        }

        private static string GenerateSalt()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToHexString(bytes);
        }

        private static string ComputeHash(string salt, string password)
        {
            byte[] input  = Encoding.UTF8.GetBytes(salt + password);
            byte[] hash   = SHA256.HashData(input);
            return Convert.ToHexString(hash);
        }
    }
}
