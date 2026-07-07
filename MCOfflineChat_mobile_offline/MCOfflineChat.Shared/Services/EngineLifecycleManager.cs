using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// v1.1.58: Coordinates engine startup/shutdown using dependency graph ordering,
/// restart policy for auto-restart decisions, and health monitor for tracking.
/// </summary>
public sealed class EngineLifecycleManager
{
    private readonly EngineOrchestrator _orchestrator;
    private readonly EngineDependencyGraph _dependencyGraph;
    private readonly EngineRestartPolicy _restartPolicy;
    private readonly EngineHealthMonitor _healthMonitor;

    public EngineLifecycleManager(
        EngineOrchestrator orchestrator,
        EngineDependencyGraph dependencyGraph,
        EngineRestartPolicy restartPolicy,
        EngineHealthMonitor healthMonitor)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _dependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        _restartPolicy = restartPolicy ?? throw new ArgumentNullException(nameof(restartPolicy));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
    }

    /// <summary>
    /// Start all engines in dependency order (dependencies first).
    /// Engines without dependencies start in registration order.
    /// </summary>
    public async Task StartAllOrderedAsync(CancellationToken ct = default)
    {
        var engineNames = _orchestrator.GetEngineNames();
        var startOrder = _dependencyGraph.GetStartOrder(engineNames);

        SglLogger.Information("[Lifecycle] Starting {Count} engines in dependency order...", startOrder.Count);

        foreach (var name in startOrder)
        {
            if (ct.IsCancellationRequested) break;

            var engine = _orchestrator.GetEngine(name);
            if (engine == null)
            {
                SglLogger.Warning("[Lifecycle] Engine {Name} in dependency graph but not registered, skipping", name);
                continue;
            }

            if (engine.IsRunning)
            {
                SglLogger.Information("[Lifecycle] Engine {Name} already running, skipping", name);
                _restartPolicy.RecordSuccess(name);
                continue;
            }

            try
            {
                await engine.StartAsync(ct);
                _restartPolicy.RecordSuccess(name);
                SglLogger.Information("[Lifecycle] Started engine: {Name}", name);
            }
            catch (Exception ex)
            {
                SglLogger.Error($"[Lifecycle] Failed to start engine {name}", ex);
            }
        }

        // Start health monitoring
        _healthMonitor.Start();
        SglLogger.Information("[Lifecycle] All engines started, health monitoring active");
    }

    /// <summary>
    /// Stop all engines in reverse dependency order (dependents first, then their dependencies).
    /// </summary>
    public async Task StopAllOrderedAsync(CancellationToken ct = default)
    {
        var engineNames = _orchestrator.GetEngineNames();
        var startOrder = _dependencyGraph.GetStartOrder(engineNames);

        // Reverse: stop dependents before their dependencies
        var stopOrder = new List<string>(startOrder);
        stopOrder.Reverse();

        SglLogger.Information("[Lifecycle] Stopping {Count} engines in reverse dependency order...", stopOrder.Count);

        // Stop health monitoring first
        _healthMonitor.Stop();

        foreach (var name in stopOrder)
        {
            if (ct.IsCancellationRequested) break;

            var engine = _orchestrator.GetEngine(name);
            if (engine == null || !engine.IsRunning) continue;

            try
            {
                await engine.StopAsync(ct);
                SglLogger.Information("[Lifecycle] Stopped engine: {Name}", name);
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[Lifecycle] Error stopping {Name}: {Error}", name, ex.Message);
            }
        }

        SglLogger.Information("[Lifecycle] All engines stopped");
    }

    /// <summary>
    /// Restart a specific engine with restart policy enforcement.
    /// Returns true if the restart succeeded, false if denied by policy or failed.
    /// </summary>
    public async Task<bool> RestartEngineAsync(string name, CancellationToken ct = default)
    {
        var engine = _orchestrator.GetEngine(name);
        if (engine == null)
        {
            SglLogger.Warning("[Lifecycle] Cannot restart unknown engine: {Name}", name);
            return false;
        }

        // Check restart policy
        if (!_restartPolicy.ShouldRestart(name))
        {
            SglLogger.Warning("[Lifecycle] Restart denied for {Name} by policy (max attempts reached)", name);
            return false;
        }

        // Record the attempt and get backoff delay
        var backoff = _restartPolicy.RecordRestart(name);
        if (backoff > TimeSpan.Zero)
        {
            SglLogger.Information("[Lifecycle] Waiting {Backoff:F1}s backoff before restarting {Name}",
                backoff.TotalSeconds, name);
            await Task.Delay(backoff, ct);
        }

        try
        {
            // Stop if running
            if (engine.IsRunning)
                await engine.StopAsync(ct);

            // Start
            await engine.StartAsync(ct);
            _restartPolicy.RecordSuccess(name);
            SglLogger.Information("[Lifecycle] Engine {Name} restarted successfully", name);
            return true;
        }
        catch (Exception ex)
        {
            SglLogger.Error($"[Lifecycle] Failed to restart engine {name}", ex);
            return false;
        }
    }
}
