// MCOfflineChat.Shared - Permission-based authorization attribute
// Fine-grained permission check for API endpoints
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Marks an endpoint as requiring a specific permission for access.
/// Used by AuthorizationMiddleware alongside RequireRoleAttribute for
/// fine-grained RBAC enforcement. The user's role is resolved from
/// HttpContext.Items["Role"] and checked against the role's permission set.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }
    public RequirePermissionAttribute(string permission) => Permission = permission;
}
