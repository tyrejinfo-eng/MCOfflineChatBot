// MCOfflineChat.Shared - RBAC Permission Constants
// Fine-grained permissions for role-based access control
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Fine-grained permissions for RBAC. Each permission follows the
/// "resource.action" convention for consistent authorization checks.
/// </summary>
public static class Permission
{
    // Alert permissions
    public const string AlertsRead = "alerts.read";
    public const string AlertsWrite = "alerts.write";
    public const string AlertsDelete = "alerts.delete";

    // Endpoint permissions
    public const string EndpointRead = "endpoint.read";
    public const string EndpointIsolate = "endpoint.isolate";
    public const string EndpointCommand = "endpoint.command";

    // LLM permissions
    public const string LlmExecute = "llm.execute";
    public const string LlmMount = "llm.mount";
    public const string LlmManage = "llm.manage";

    // Engine permissions
    public const string EngineRead = "engine.read";
    public const string EngineControl = "engine.control";

    // Admin permissions
    public const string AdminManage = "admin.manage";
    public const string AdminAudit = "admin.audit";

    // Investigation permissions
    public const string InvestigationRead = "investigation.read";
    public const string InvestigationWrite = "investigation.write";

    // Telemetry permissions
    public const string TelemetryRead = "telemetry.read";
    public const string TelemetryWrite = "telemetry.write";

    // Federation permissions
    public const string FederationManage = "federation.manage";
    public const string FederationRead = "federation.read";

    // Swarm permissions
    public const string SwarmManage = "swarm.manage";
    public const string SwarmRead = "swarm.read";

    /// <summary>
    /// Returns all defined permissions via reflection-free static list.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        AlertsRead, AlertsWrite, AlertsDelete,
        EndpointRead, EndpointIsolate, EndpointCommand,
        LlmExecute, LlmMount, LlmManage,
        EngineRead, EngineControl,
        AdminManage, AdminAudit,
        InvestigationRead, InvestigationWrite,
        TelemetryRead, TelemetryWrite,
        FederationManage, FederationRead,
        SwarmManage, SwarmRead
    };
}
