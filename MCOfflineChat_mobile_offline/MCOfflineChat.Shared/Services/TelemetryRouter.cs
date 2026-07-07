using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Fan-out telemetry router that dispatches events to multiple consumers in parallel
/// via Task.WhenAll. Replaces sequential EventBus wildcard subscriptions for the
/// core telemetry pipeline, ensuring StorageRouter, EvidenceGraph, ML Pipeline,
/// and StateRegistry all receive events concurrently.
/// </summary>
public sealed class TelemetryRouter
{
    private readonly List<Func<TelemetryEvent, Task>> _consumers = [];
    private long _totalRouted;
    private long _totalErrors;

    public long TotalRouted => Interlocked.Read(ref _totalRouted);
    public long TotalErrors => Interlocked.Read(ref _totalErrors);
    public int ConsumerCount => _consumers.Count;

    /// <summary>Register a consumer that receives every telemetry event.</summary>
    public void AddConsumer(Func<TelemetryEvent, Task> consumer)
    {
        if (consumer == null) return;
        _consumers.Add(consumer);
    }

    /// <summary>
    /// Route an event to all registered consumers in parallel.
    /// Individual consumer failures do not prevent other consumers from receiving the event.
    /// </summary>
    public async Task RouteAsync(TelemetryEvent evt)
    {
        if (_consumers.Count == 0) return;

        var tasks = new Task[_consumers.Count];
        for (int i = 0; i < _consumers.Count; i++)
        {
            var consumer = _consumers[i];
            tasks[i] = SafeInvoke(consumer, evt);
        }

        await Task.WhenAll(tasks);
        Interlocked.Increment(ref _totalRouted);
    }

    private async Task SafeInvoke(Func<TelemetryEvent, Task> consumer, TelemetryEvent evt)
    {
        try
        {
            if (consumer == null || evt == null) return;
            await consumer(evt);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalErrors);
            SglLogger.Warning("[TelemetryRouter] Consumer error: {Error}", ex.Message);
        }
    }
}
