// MCOfflineChat.Shared - RBAC Role Definitions
// Maps roles to their granted permissions
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Defines RBAC roles and their associated permission sets.
/// Each role maps to a static read-only list of <see cref="Permission"/> constants.
/// </summary>
public static class Role
{
    public const string Admin = "Admin";
    public const string SOCAnalyst = "SOCAnalyst";
    public const string ThreatHunter = "ThreatHunter";
    public const string AutomationOperator = "AutomationOperator";
    public const string Viewer = "Viewer";
    public const string APIClient = "APIClient";

    /// <summary>Admin — full access to all permissions.</summary>
    public static IReadOnlyList<string> AdminPermissions { get; } = Permission.All;

    /// <summary>SOC Analyst — alerts read/write, investigations, endpoint read, telemetry read, engine read.</summary>
    public static IReadOnlyList<string> SOCAnalystPermissions { get; } = new[]
    {
        Permission.AlertsRead,
        Permission.AlertsWrite,
        Permission.InvestigationRead,
        Permission.InvestigationWrite,
        Permission.EndpointRead,
        Permission.TelemetryRead,
        Permission.EngineRead
    };

    /// <summary>Threat Hunter — alerts, investigations, telemetry, endpoint read.</summary>
    public static IReadOnlyList<string> ThreatHunterPermissions { get; } = new[]
    {
        Permission.AlertsRead,
        Permission.AlertsWrite,
        Permission.AlertsDelete,
        Permission.InvestigationRead,
        Permission.InvestigationWrite,
        Permission.TelemetryRead,
        Permission.TelemetryWrite,
        Permission.EndpointRead
    };

    /// <summary>Automation Operator — engines, LLM, alerts read, endpoint isolate.</summary>
    public static IReadOnlyList<string> AutomationOperatorPermissions { get; } = new[]
    {
        Permission.EngineRead,
        Permission.EngineControl,
        Permission.LlmExecute,
        Permission.LlmMount,
        Permission.LlmManage,
        Permission.AlertsRead,
        Permission.EndpointIsolate
    };

    /// <summary>Viewer — read-only access across all resources.</summary>
    public static IReadOnlyList<string> ViewerPermissions { get; } = new[]
    {
        Permission.AlertsRead,
        Permission.EndpointRead,
        Permission.EngineRead,
        Permission.InvestigationRead,
        Permission.TelemetryRead,
        Permission.FederationRead,
        Permission.SwarmRead
    };

    /// <summary>API Client — limited machine-to-machine access.</summary>
    public static IReadOnlyList<string> APIClientPermissions { get; } = new[]
    {
        Permission.AlertsRead,
        Permission.TelemetryWrite,
        Permission.EndpointRead
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> _rolePermissionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Admin] = AdminPermissions,
        [SOCAnalyst] = SOCAnalystPermissions,
        [ThreatHunter] = ThreatHunterPermissions,
        [AutomationOperator] = AutomationOperatorPermissions,
        [Viewer] = ViewerPermissions,
        [APIClient] = APIClientPermissions
    };

    /// <summary>
    /// Resolves the permission list for a given role name (case-insensitive).
    /// Returns an empty list for unknown roles.
    /// </summary>
    public static IReadOnlyList<string> GetPermissions(string roleName)
    {
        return _rolePermissionMap.TryGetValue(roleName, out var perms)
            ? perms
            : Array.Empty<string>();
    }

    /// <summary>
    /// Checks whether a role has a specific permission.
    /// </summary>
    public static bool HasPermission(string roleName, string permission)
    {
        var perms = GetPermissions(roleName);
        for (int i = 0; i < perms.Count; i++)
        {
            if (string.Equals(perms[i], permission, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns all defined role names.
    /// </summary>
    public static IReadOnlyList<string> AllRoles { get; } = new[]
    {
        Admin, SOCAnalyst, ThreatHunter, AutomationOperator, Viewer, APIClient
    };
}
