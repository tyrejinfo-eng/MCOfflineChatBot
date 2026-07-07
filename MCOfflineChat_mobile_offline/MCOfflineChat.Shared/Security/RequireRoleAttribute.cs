namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Marks an endpoint as requiring a specific role for access.
/// Used by AuthorizationMiddleware for centralized role enforcement.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute : Attribute
{
    public string Role { get; }
    public RequireRoleAttribute(string role) => Role = role;
}
