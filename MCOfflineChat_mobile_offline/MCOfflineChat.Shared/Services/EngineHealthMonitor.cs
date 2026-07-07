using System.Collections.Concurrent;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// v1.1.58: Background service that checks engine health every 30 seconds.
/// Tracks LastHeartbeat for ICapableEngine implementations and IsRunning for standard IEngine.
/// </summary>
public sealed class EngineHealthMonitor : IDisposable
{
    private const int CheckIntervalSeconds = 30;

    private readonly EngineOrchestrator _orchestrator;
    private readonly ConcurrentDictionary<string, EngineHealthInfo> _healthRecords = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;

    public EngineHealthMonitor(EngineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    /// <summary>Start periodic health monitoring.</summary>
    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => _ = CheckHealthAsync(), null,
            TimeSpan.FromSeconds(CheckIntervalSeconds), TimeSpan.FromSeconds(CheckIntervalSeconds));
        SglLogger.Information("[HealthMonitor] Started (interval={Interval}s)", CheckIntervalSeconds);
    }

    /// <summary>Stop health monitoring.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        SglLogger.Information("[HealthMonitor] Stopped");
    }

    private async Task CheckHealthAsync()
    {
        try
        {
            var statuses = _orchestrator.GetAllStatus();
            foreach (var status in statuses)
            {
                var info = _healthRecords.GetOrAdd(status.EngineName, _ => new EngineHealthInfo());
                info.EngineName = status.EngineName;
                info.IsRunning = status.IsRunning;
                info.LastChecked = DateTime.UtcNow;
                info.EventsProcessed = status.EventsProcessed;
                info.ErrorCount = status.Errors;

                // For ICapableEngine, call HealthCheckAsync and track heartbeat
                var engine = _orchestrator.GetEngine(status.EngineName);
                if (engine is ICapableEngine capable)
                {
                    try
                    {
                        info.IsHealthy = await capable.HealthCheckAsync();
                        info.LastHeartbeat = capable.LastHeartbeat;
                        info.Capabilities = capable.Capabilities;
                        if (info.IsHealthy)
                            info.LastHealthy = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        info.IsHealthy = false;
                        SglLogger.Warning("[HealthMonitor] HealthCheck failed for {Engine}: {Error}",
                            status.EngineName, ex.Message);
                    }
                }
                else
                {
                    // Standard IEngine — healthy if running
                    info.IsHealthy = status.IsRunning;
                    if (status.IsRunning)
                        info.LastHealthy = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[HealthMonitor] Health check cycle failed", ex);
        }
    }

    /// <summary>Get health report for all monitored engines.</summary>
    public IReadOnlyDictionary<string, EngineHealthInfo> GetHealthReport()
    {
        return _healthRecords.ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>Check if a specific engine is healthy.</summary>
    public bool IsHealthy(string engineName)
    {
        return _healthRecords.TryGetValue(engineName, out var info) && info.IsHealthy;
    }

    public void Dispose() => Stop();
}

/// <summary>Health information snapshot for a single engine.</summary>
public class EngineHealthInfo
{
    public string EngineName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsHealthy { get; set; }
    public DateTime LastChecked { get; set; }
    public DateTime? LastHealthy { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public long EventsProcessed { get; set; }
    public long ErrorCount { get; set; }
    public EngineCapability Capabilities { get; set; }
}
