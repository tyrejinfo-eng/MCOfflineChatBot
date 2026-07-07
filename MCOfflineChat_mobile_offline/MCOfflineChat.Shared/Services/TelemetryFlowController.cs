using System.Threading.Channels;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Adaptive flow controller for the telemetry ingestion pipeline.
/// Uses System.Threading.Channels for high-throughput bounded backpressure.
/// When the pipeline is overloaded, automatically applies adaptive sampling
/// to prevent memory exhaustion while preserving high-severity events.
/// </summary>
public sealed class TelemetryFlowController : IDisposable
{
    private readonly Channel<TelemetryEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly Func<TelemetryEvent, Task> _processor;

    // Adaptive sampling state
    private long _totalSubmitted;
    private long _totalProcessed;
    private long _totalDropped;
    private long _totalSampled;

    public long TotalSubmitted => Interlocked.Read(ref _totalSubmitted);
    public long TotalProcessed => Interlocked.Read(ref _totalProcessed);
    public long TotalDropped => Interlocked.Read(ref _totalDropped);
    public long TotalSampled => Interlocked.Read(ref _totalSampled);
    public int QueueDepth => _channel.Reader.Count;
    public double LoadFactor => QueueDepth / (double)_capacity;
    public bool IsRunning { get; private set; }

    private readonly int _capacity;

    /// <param name="processor">Delegate called for each event that passes the flow gate.</param>
    /// <param name="capacity">Maximum channel capacity before backpressure kicks in.</param>
    public TelemetryFlowController(Func<TelemetryEvent, Task> processor, int capacity = 100_000)
    {
        _processor = processor;
        _capacity = capacity;
        _channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        IsRunning = true;
        _workerTask = Task.Run(() => ProcessLoop(_cts.Token));
        SglLogger.Information("[FlowController] Started with capacity {Cap}", capacity);
    }

    /// <summary>
    /// Submit a telemetry event to the pipeline. Applies adaptive sampling
    /// when load exceeds 80% capacity.
    /// </summary>
    public ValueTask SubmitAsync(TelemetryEvent evt)
    {
        Interlocked.Increment(ref _totalSubmitted);

        // Adaptive sampling under load
        var load = LoadFactor;
        if (load > 0.8)
        {
            // Always keep Critical/Error events
            if (evt.Severity is not ("Critical" or "Error"))
            {
                // Drop rate scales with load: 30% at 80% load → 70% at 100%
                var dropRate = (load - 0.8) * 3.5;
                if (Random.Shared.NextDouble() < dropRate)
                {
                    Interlocked.Increment(ref _totalSampled);
                    return ValueTask.CompletedTask;
                }
            }
        }

        if (_channel.Writer.TryWrite(evt))
            return ValueTask.CompletedTask;

        Interlocked.Increment(ref _totalDropped);
        return ValueTask.CompletedTask;
    }

    private async Task ProcessLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _processor(evt);
                    Interlocked.Increment(ref _totalProcessed);
                }
                catch (Exception ex)
                {
                    SglLogger.Warning("[FlowController] Process error: {Msg}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { _workerTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
        _cts.Dispose();
        SglLogger.Information("[FlowController] Stopped. Submitted={Sub}, Processed={Proc}, Dropped={Drop}, Sampled={Samp}",
            TotalSubmitted, TotalProcessed, TotalDropped, TotalSampled);
    }
}
