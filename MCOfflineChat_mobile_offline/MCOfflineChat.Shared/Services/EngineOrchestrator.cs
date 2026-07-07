using System.Collections.Concurrent;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Central orchestrator for all engines in the MC Offline Chat platform.
/// Manages engine lifecycle (start/stop/restart), provides status queries
/// for the admin console, and ensures graceful startup ordering.
/// </summary>
public sealed class EngineOrchestrator : IDisposable
{
    private readonly ConcurrentDictionary<string, IEngine> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EngineStatus> _statusCache = new();
    private bool _isStarted;

    /// <summary>Total registered engines.</summary>
    public int EngineCount => _engines.Count;

    /// <summary>Number of currently running engines.</summary>
    public int RunningCount => _engines.Values.Count(e => e.IsRunning);

    /// <summary>Register an engine for orchestration.</summary>
    public void Register(IEngine engine)
    {
        if (_engines.TryAdd(engine.Name, engine))
            SglLogger.Information("[Orchestrator] Registered engine: {Name}", engine.Name);
    }

    /// <summary>Start all registered engines in registration order.</summary>
    public async Task StartAllAsync(CancellationToken ct = default)
    {
        SglLogger.Information("[Orchestrator] Starting {Count} engines...", _engines.Count);
        foreach (var (name, engine) in _engines)
        {
            try
            {
                await engine.StartAsync(ct);
                SglLogger.Information("[Orchestrator] Engine started: {Name}", name);
            }
            catch (Exception ex)
            {
                SglLogger.Error($"[Orchestrator] Failed to start {name}", ex);
            }
        }
        _isStarted = true;
    }

    /// <summary>Stop all running engines gracefully.</summary>
    public async Task StopAllAsync(CancellationToken ct = default)
    {
        SglLogger.Information("[Orchestrator] Stopping all engines...");
        foreach (var (name, engine) in _engines.Reverse())
        {
            try
            {
                if (engine.IsRunning)
                    await engine.StopAsync(ct);
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[Orchestrator] Error stopping {Name}: {Error}", name, ex.Message);
            }
        }
        _isStarted = false;
    }

    /// <summary>Start a specific engine by name.</summary>
    public async Task<bool> StartEngineAsync(string name, CancellationToken ct = default)
    {
        if (!_engines.TryGetValue(name, out var engine))
            return false;
        await engine.StartAsync(ct);
        return true;
    }

    /// <summary>Stop a specific engine by name.</summary>
    public async Task<bool> StopEngineAsync(string name, CancellationToken ct = default)
    {
        if (!_engines.TryGetValue(name, out var engine))
            return false;
        await engine.StopAsync(ct);
        return true;
    }

    /// <summary>Restart a specific engine.</summary>
    public async Task<bool> RestartEngineAsync(string name, CancellationToken ct = default)
    {
        if (!_engines.TryGetValue(name, out var engine))
            return false;
        if (engine.IsRunning)
            await engine.StopAsync(ct);
        await engine.StartAsync(ct);
        return true;
    }

    /// <summary>Get status of all engines.</summary>
    public IReadOnlyList<EngineStatus> GetAllStatus()
    {
        return _engines.Values.Select(e =>
        {
            try { return e.GetStatus(); }
            catch { return new EngineStatus { EngineName = e.Name, IsRunning = e.IsRunning }; }
        }).ToList();
    }

    /// <summary>Get a specific engine's status.</summary>
    public EngineStatus? GetEngineStatus(string name)
    {
        return _engines.TryGetValue(name, out var engine) ? engine.GetStatus() : null;
    }

    /// <summary>Get all engine names.</summary>
    public IReadOnlyList<string> GetEngineNames() => _engines.Keys.ToList();

    /// <summary>Get an engine instance by name.</summary>
    public IEngine? GetEngine(string name)
    {
        return _engines.TryGetValue(name, out var engine) ? engine : null;
    }

    public void Dispose()
    {
        if (_isStarted)
            StopAllAsync().GetAwaiter().GetResult();
    }
}
