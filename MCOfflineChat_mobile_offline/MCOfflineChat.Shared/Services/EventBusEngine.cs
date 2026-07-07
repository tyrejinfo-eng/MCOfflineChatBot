using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// IEngine adapter that wraps the EventBus for orchestrator management.
/// The EventBus itself is always running — this adapter provides status and metrics.
/// </summary>
public sealed class EventBusEngine : IEngine
{
    private readonly EventBus _eventBus;
    private DateTime? _startedAt;

    public string Name => "EventBus";
    public bool IsRunning { get; private set; }

    public EventBusEngine(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        _startedAt ??= DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }

    public EngineStatus GetStatus() => new()
    {
        EngineName = Name,
        IsRunning = IsRunning,
        StartedAt = _startedAt,
        EventsProcessed = _eventBus.PublishedCount,
        Metrics = new Dictionary<string, object>
        {
            ["published"] = _eventBus.PublishedCount,
            ["dispatched"] = _eventBus.DispatchedCount,
            ["dropped"] = _eventBus.DroppedCount,
            ["errors"] = _eventBus.ErrorCount,
            ["pending"] = _eventBus.PendingCount,
            ["subscribers"] = _eventBus.SubscriberCount,
            ["unsigned"] = _eventBus.UnsignedCount,
            ["expired"] = _eventBus.ExpiredCount,
            ["dlq"] = _eventBus.DlqCount,
            ["stale"] = _eventBus.StaleCount
        }
    };
}
