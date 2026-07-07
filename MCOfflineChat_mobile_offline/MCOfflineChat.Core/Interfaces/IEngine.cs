namespace MCOfflineChat.Core.Interfaces;

/// <summary>
/// Contract for all engines in the MC Offline Chat platform.
/// Engines are long-lived services that process security telemetry,
/// perform analysis, or provide infrastructure capabilities.
/// </summary>
public interface IEngine
{
    /// <summary>Unique engine identifier (e.g., "ThreatGraph", "ML", "ASRE").</summary>
    string Name { get; }

    /// <summary>Whether the engine is currently processing events.</summary>
    bool IsRunning { get; }

    /// <summary>Start the engine. Idempotent — safe to call on an already-running engine.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Gracefully stop the engine, flushing any pending work.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Get current engine health and metrics.</summary>
    EngineStatus GetStatus();
}

/// <summary>
/// Snapshot of an engine's current state for monitoring and admin dashboards.
/// </summary>
public class EngineStatus
{
    public string EngineName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public DateTime? StartedAt { get; set; }
    public long EventsProcessed { get; set; }
    public long Errors { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = [];
}
