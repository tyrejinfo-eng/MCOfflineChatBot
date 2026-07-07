using System.Collections.Concurrent;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Self-healing watchdog that monitors engine health via the orchestrator
/// and auto-restarts engines that have unexpectedly stopped.
/// </summary>
public sealed class EngineWatchdog : IDisposable
{
    private const int CheckIntervalSeconds = 60;
    private const int MaxRestartsPerHour = 3;

    private readonly EngineOrchestrator _orchestrator;
    private readonly EventBus? _eventBus;
    private readonly ConcurrentDictionary<string, bool> _previousState = new();
    private readonly ConcurrentDictionary<string, RestartRecord> _restartRecords = new();
    private Timer? _timer;
    private long _totalRestartAttempts;

    public EngineWatchdog(EngineOrchestrator orchestrator, EventBus? eventBus = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _eventBus = eventBus;
    }

    public void StartMonitoring()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => _ = CheckEnginesAsync(), null,
            TimeSpan.FromSeconds(CheckIntervalSeconds), TimeSpan.FromSeconds(CheckIntervalSeconds));
        SglLogger.Information("[Watchdog] Monitoring started (interval={Interval}s)", CheckIntervalSeconds);
    }

    public void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
        SglLogger.Information("[Watchdog] Monitoring stopped");
    }

    private async Task CheckEnginesAsync()
    {
        try
        {
            var statuses = _orchestrator.GetAllStatus();
            foreach (var status in statuses)
            {
                bool wasRunning = _previousState.GetValueOrDefault(status.EngineName, false);
                _previousState[status.EngineName] = status.IsRunning;

                if (wasRunning && !status.IsRunning)
                    await TryRestartAsync(status.EngineName);
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[Watchdog] Check cycle failed", ex);
        }
    }

    private async Task TryRestartAsync(string engineName)
    {
        var record = _restartRecords.GetOrAdd(engineName, _ => new RestartRecord());

        // Reset hourly counter
        if ((DateTime.UtcNow - record.HourWindowStart).TotalHours >= 1)
        {
            record.RestartCount = 0;
            record.HourWindowStart = DateTime.UtcNow;
        }

        if (record.RestartCount >= MaxRestartsPerHour)
        {
            SglLogger.Warning("[Watchdog] {Engine} exceeded max restarts ({Max}/hr), skipping",
                engineName, MaxRestartsPerHour);
            return;
        }

        double backoffSeconds = Math.Min(60, Math.Pow(2, record.ConsecutiveFailures));
        if ((DateTime.UtcNow - record.LastRestartAttempt).TotalSeconds < backoffSeconds)
            return;

        record.LastRestartAttempt = DateTime.UtcNow;
        record.RestartCount++;
        Interlocked.Increment(ref _totalRestartAttempts);

        SglLogger.Warning("[Watchdog] Engine {Engine} stopped unexpectedly, attempting restart #{Attempt}",
            engineName, record.RestartCount);

        _eventBus?.Publish("engine.watchdog.restart", "EngineWatchdog", "Warning",
            new Dictionary<string, object> { ["engine"] = engineName, ["attempt"] = record.RestartCount });

        try
        {
            bool success = await _orchestrator.RestartEngineAsync(engineName);
            if (success)
            {
                record.ConsecutiveFailures = 0;
                record.LastSuccessfulRestart = DateTime.UtcNow;
                SglLogger.Information("[Watchdog] Engine {Engine} restarted successfully", engineName);
            }
            else
            {
                record.ConsecutiveFailures++;
                SglLogger.Warning("[Watchdog] Engine {Engine} restart returned false", engineName);
            }
        }
        catch (Exception ex)
        {
            record.ConsecutiveFailures++;
            SglLogger.Error($"[Watchdog] Failed to restart {engineName}", ex);
        }
    }

    public WatchdogStatus GetWatchdogStatus() => new()
    {
        IsMonitoring = _timer != null,
        CheckIntervalSeconds = CheckIntervalSeconds,
        RestartRecords = _restartRecords.ToDictionary(kv => kv.Key, kv => kv.Value),
        TotalRestartAttempts = Interlocked.Read(ref _totalRestartAttempts)
    };

    public void Dispose() => StopMonitoring();
}

public class RestartRecord
{
    public int RestartCount { get; set; }
    public DateTime LastRestartAttempt { get; set; }
    public DateTime? LastSuccessfulRestart { get; set; }
    public int ConsecutiveFailures { get; set; }
    internal DateTime HourWindowStart { get; set; } = DateTime.UtcNow;
}

public class WatchdogStatus
{
    public bool IsMonitoring { get; init; }
    public int CheckIntervalSeconds { get; init; }
    public Dictionary<string, RestartRecord> RestartRecords { get; init; } = [];
    public long TotalRestartAttempts { get; init; }
}
