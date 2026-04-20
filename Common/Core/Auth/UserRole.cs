namespace Common.Core.Auth
{
    public enum UserRole
    {
        Admin,    // Full access: all modules + user management
        Operator, // All modules, no user management
        Viewer    // Read-only modules only (Led, BMS)
    }
}
