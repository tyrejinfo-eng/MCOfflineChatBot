namespace MCOfflineChat.Core.Interfaces;

/// <summary>
/// v1.1.58: Extended engine interface with capability declaration and health monitoring.
/// Engines can optionally implement this for enhanced orchestration and event routing.
/// Engines that only implement IEngine continue to work with the orchestrator.
/// </summary>
public interface ICapableEngine : IEngine
{
    /// <summary>Capabilities this engine provides for event routing (flags enum).</summary>
    EngineCapability Capabilities { get; }

    /// <summary>Last heartbeat timestamp, updated by the engine's processing loop.</summary>
    DateTime LastHeartbeat { get; }

    /// <summary>Perform a health check. Returns true if healthy.</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}
