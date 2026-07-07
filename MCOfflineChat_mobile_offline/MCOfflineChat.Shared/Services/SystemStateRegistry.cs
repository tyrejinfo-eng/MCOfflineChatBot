using System.Collections.Concurrent;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Central system state registry — the single source of truth for all runtime state
/// in the MC Offline Chat platform. Tracks engines, agents, models, and pipeline health.
/// Used by the Admin Console, Control Plane API, and monitoring endpoints.
/// </summary>
public sealed class SystemStateRegistry
{
    private readonly ConcurrentDictionary<string, AgentRegistration> _agents = new();
    private readonly ConcurrentDictionary<string, ModelState> _models = new();
    private readonly EngineOrchestrator _orchestrator;
    private readonly DateTime _bootTime = DateTime.UtcNow;

    // Pipeline metrics
    private long _totalTelemetryIngested;
    private long _totalTelemetryProcessed;
    private long _totalThreatDetections;
    private long _totalResponseActions;

    public long TotalTelemetryIngested => Interlocked.Read(ref _totalTelemetryIngested);
    public long TotalTelemetryProcessed => Interlocked.Read(ref _totalTelemetryProcessed);
    public long TotalThreatDetections => Interlocked.Read(ref _totalThreatDetections);
    public long TotalResponseActions => Interlocked.Read(ref _totalResponseActions);
    public DateTime BootTime => _bootTime;
    public TimeSpan Uptime => DateTime.UtcNow - _bootTime;
    public int ConnectedAgentCount => _agents.Count;
    public int LoadedModelCount => _models.Values.Count(m => m.IsLoaded);

    public SystemStateRegistry(EngineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    // ── Agent Registry ─────────────────────────────────────────

    public void RegisterAgent(string agentId, string hostname, string version)
    {
        _agents.AddOrUpdate(agentId,
            new AgentRegistration(agentId, hostname, version),
            (_, existing) => { existing.LastHeartbeat = DateTime.UtcNow; return existing; });
    }

    public void RemoveAgent(string agentId) => _agents.TryRemove(agentId, out _);

    public IReadOnlyList<AgentRegistration> GetAllAgents() => _agents.Values.ToList();

    public void AgentHeartbeat(string agentId)
    {
        if (_agents.TryGetValue(agentId, out var agent))
            agent.LastHeartbeat = DateTime.UtcNow;
    }

    /// <summary>Prune agents not seen within the timeout period.</summary>
    public int PruneStaleAgents(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var stale = _agents.Where(kv => kv.Value.LastHeartbeat < cutoff).Select(kv => kv.Key).ToList();
        foreach (var id in stale)
            _agents.TryRemove(id, out _);
        return stale.Count;
    }

    // ── Model Registry ─────────────────────────────────────────

    public void RegisterModel(string modelId, string path, string role)
    {
        _models[modelId] = new ModelState(modelId, path, role);
    }

    public void MarkModelLoaded(string modelId, int slot)
    {
        if (_models.TryGetValue(modelId, out var model))
        {
            model.IsLoaded = true;
            model.Slot = slot;
            model.LoadedAt = DateTime.UtcNow;
        }
    }

    public void MarkModelUnloaded(string modelId)
    {
        if (_models.TryGetValue(modelId, out var model))
        {
            model.IsLoaded = false;
            model.Slot = null;
        }
    }

    public IReadOnlyList<ModelState> GetAllModels() => _models.Values.ToList();

    // ── Pipeline Metrics ───────────────────────────────────────

    public void RecordTelemetryIngested() => Interlocked.Increment(ref _totalTelemetryIngested);
    public void RecordTelemetryProcessed() => Interlocked.Increment(ref _totalTelemetryProcessed);
    public void RecordThreatDetection() => Interlocked.Increment(ref _totalThreatDetections);
    public void RecordResponseAction() => Interlocked.Increment(ref _totalResponseActions);

    // ── Composite State Snapshot ───────────────────────────────

    public SystemStateSnapshot GetSnapshot()
    {
        return new SystemStateSnapshot
        {
            BootTime = _bootTime,
            Uptime = Uptime,
            Engines = _orchestrator.GetAllStatus(),
            RunningEngines = _orchestrator.RunningCount,
            TotalEngines = _orchestrator.EngineCount,
            ConnectedAgents = _agents.Count,
            LoadedModels = _models.Values.Count(m => m.IsLoaded),
            TotalTelemetryIngested = TotalTelemetryIngested,
            TotalTelemetryProcessed = TotalTelemetryProcessed,
            TotalThreatDetections = TotalThreatDetections,
            TotalResponseActions = TotalResponseActions
        };
    }
}

// ── Supporting types ──────────────────────────────────────────

public class AgentRegistration
{
    public string AgentId { get; }
    public string Hostname { get; }
    public string Version { get; }
    public DateTime RegisteredAt { get; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public bool IsStale(TimeSpan timeout) => DateTime.UtcNow - LastHeartbeat > timeout;

    public AgentRegistration(string agentId, string hostname, string version)
    {
        AgentId = agentId;
        Hostname = hostname;
        Version = version;
    }
}

public class ModelState
{
    public string ModelId { get; }
    public string Path { get; }
    public string Role { get; }
    public bool IsLoaded { get; set; }
    public int? Slot { get; set; }
    public DateTime? LoadedAt { get; set; }

    public ModelState(string modelId, string path, string role)
    {
        ModelId = modelId;
        Path = path;
        Role = role;
    }
}

public class SystemStateSnapshot
{
    public DateTime BootTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public IReadOnlyList<EngineStatus> Engines { get; set; } = [];
    public int RunningEngines { get; set; }
    public int TotalEngines { get; set; }
    public int ConnectedAgents { get; set; }
    public int LoadedModels { get; set; }
    public long TotalTelemetryIngested { get; set; }
    public long TotalTelemetryProcessed { get; set; }
    public long TotalThreatDetections { get; set; }
    public long TotalResponseActions { get; set; }
}
