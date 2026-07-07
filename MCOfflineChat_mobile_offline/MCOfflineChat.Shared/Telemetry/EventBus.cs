using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Telemetry;

/// <summary>
/// Represents a telemetry event flowing through the EventBus.
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>Unique event identifier for deduplication.</summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Data { get; set; } = [];
    public string Severity { get; set; } = "Info";
    public string? CorrelationId { get; set; }

    // Enhanced fields for telemetry pipeline, evidence graph, and investigation engine
    public string? HostId { get; set; }
    public string? ProcessId { get; set; }
    public string? ParentProcessId { get; set; }
    public string? FileHash { get; set; }
    public string? CommandLine { get; set; }
    public string? RemoteAddress { get; set; }
    public int? RemotePort { get; set; }

    /// <summary>v1.1.72: Event priority for dispatch ordering.</summary>
    public EventPriority Priority { get; set; } = EventPriority.Normal;

    /// <summary>v1.1.62: HMAC-SHA256 signature for event integrity verification.</summary>
    public string? Signature { get; set; }

    /// <summary>Time-to-live for this event as a TimeSpan. If set, takes precedence over TimeToLiveSeconds.</summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>Time-to-live in seconds. Default 300 (5 minutes). Used when Ttl is null.</summary>
    public int TimeToLiveSeconds { get; set; } = 300;

    /// <summary>Helper to pull a typed value from Data.</summary>
    public T? Get<T>(string key) =>
        Data.TryGetValue(key, out var val) && val is T typed ? typed : default;

    /// <summary>Resolves the effective TTL, preferring Ttl if explicitly set, otherwise TimeToLiveSeconds.</summary>
    internal TimeSpan GetEffectiveTtl() => Ttl ?? TimeSpan.FromSeconds(TimeToLiveSeconds);
}

/// <summary>v1.1.72: Priority levels for EventBus dispatch.</summary>
public enum EventPriority { Critical = 0, High = 1, Normal = 2, Low = 3 }

/// <summary>
/// Thread-safe in-process publish / subscribe event bus with dedicated dispatch thread.
/// Subscribers receive events asynchronously on a background thread (not the publisher's thread).
/// </summary>
public sealed class EventBus : IDisposable
{
    private readonly ConcurrentDictionary<string, List<Func<TelemetryEvent, Task>>> _handlers = new();

    // v1.1.73: True priority queue dispatch — lower (int)Priority = dequeued first.
    private readonly PriorityQueue<TelemetryEvent, int> _priorityQueue = new();
    private readonly SemaphoreSlim _prioritySignal = new(0);
    private const int PriorityQueueCapacity = 50_000;

    private readonly Thread[] _dispatchWorkers;
    private const int WorkerCount = 4;
    private readonly CancellationTokenSource _cts = new();

    // Metrics
    private long _publishedCount;
    private long _dispatchedCount;
    private long _droppedCount;
    private long _errorCount;
    private long _unsignedCount;
    private long _expiredCount;
    private long _dlqCount;
    private long _staleCount;
    private long _circuitBreakerTrips;

    // Event deduplication — LRU via ConcurrentDictionary (lock-free, no HashSet/Queue needed)
    private readonly ConcurrentDictionary<string, DateTime> _seenEventIds = new();
    private const int DedupWindowSize = 10_000;
    private long _deduplicatedCount;
    private long _replayedCount;

    // WAL (write-ahead log) for crash recovery — append-only JSONL; graceful-degrades to in-memory
    private readonly string _walDir;
    private readonly string _walPath;
    private readonly object _walLock = new();
    private volatile bool _walEnabled;

    // DLQ auto-replay scheduler (every 60 s, exponential backoff per file)
    private Timer? _dlqReplayTimer;
    private readonly ConcurrentDictionary<string, (int RetryCount, DateTime LastAttempt)> _dlqRetryMeta = new();

    // Atomic counters for pending queue depth and subscriber count
    private long _pendingCount;
    private int _subscriberCount;

    /// <summary>Optional HMAC signing verifier. When set, unsigned events are logged as warnings.</summary>
    private Security.EventBusSigning? _signing;

    /// <summary>v1.1.91: Optional durable event stream for replay capability.</summary>
    private MCOfflineChat.Shared.Services.EventStreamWriter? _eventStream;

    // Dead-Letter Queue - individual JSON files in data/dlq/
    private readonly string _dlqDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "dlq");
    private readonly object _dlqLock = new();
    private const int DlqMaxFiles = 10_000;
    private bool _dlqDirCreated;

    // v1.1.74: DPAPI-protected AES-256-GCM key for DLQ at rest (replaces deterministic SHA256 key)
    private static byte[]? _dlqEncryptionKey;
    private static readonly object _dlqKeyLock = new();

    // Subscriber Circuit Breaker: track consecutive failures and pause-until time per handler hashcode
    private readonly ConcurrentDictionary<int, int> _handlerFailures = new();
    private readonly ConcurrentDictionary<int, DateTime> _handlerPausedUntil = new();
    private const int CircuitBreakerThreshold = 5;
    private static readonly TimeSpan CircuitBreakerPause = TimeSpan.FromSeconds(60);

    // Per-event-type latency tracking
    private readonly ConcurrentDictionary<string, (long totalMs, long count)> _latencyStats = new();

    public long PublishedCount => Interlocked.Read(ref _publishedCount);
    public long DispatchedCount => Interlocked.Read(ref _dispatchedCount);
    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public long ErrorCount => Interlocked.Read(ref _errorCount);

    /// <summary>Count of events published without HMAC signature.</summary>
    public long UnsignedCount => Interlocked.Read(ref _unsignedCount);

    /// <summary>Count of events discarded because they exceeded their TTL.</summary>
    public long ExpiredCount => Interlocked.Read(ref _expiredCount);

    /// <summary>Count of events written to the dead-letter queue.</summary>
    public long DlqCount => Interlocked.Read(ref _dlqCount);

    /// <summary>Count of stale events skipped during dispatch (older than TTL).</summary>
    public long StaleCount => Interlocked.Read(ref _staleCount);

    /// <summary>Total number of times circuit breakers have tripped across all handlers.</summary>
    public long CircuitBreakerTrips => Interlocked.Read(ref _circuitBreakerTrips);

    /// <summary>Count of events skipped due to deduplication (duplicate EventId).</summary>
    public long DeduplicatedCount => Interlocked.Read(ref _deduplicatedCount);

    /// <summary>Count of events successfully replayed from the DLQ.</summary>
    public long ReplayedCount => Interlocked.Read(ref _replayedCount);

    /// <summary>Current queue pressure as a ratio (0.0 to 1.0). Above 0.8 triggers OnHighPressure.</summary>
    public double QueuePressure => (double)PendingCount / PriorityQueueCapacity;

    /// <summary>Fired when queue pressure exceeds 80%. Subscribers should reduce publish rate.</summary>
    public event Action? OnHighPressure;

    /// <summary>
    /// v1.1.62: Enable HMAC signature verification for events.
    /// When set, unsigned events are still dispatched but logged as warnings.
    /// </summary>
    public void SetSigning(Security.EventBusSigning signing) => _signing = signing;

    /// <summary>
    /// v1.1.91: Wire a durable EventStreamWriter so every dispatched event is appended
    /// for replay capability. Call once during application startup.
    /// </summary>
    public void SetEventStream(MCOfflineChat.Shared.Services.EventStreamWriter stream)
    {
        _eventStream = stream;
    }

    /// <summary>
    /// When true, events without a valid HMAC signature are dropped (not just warned).
    /// Default: false for backward compatibility.
    /// </summary>
    public bool EnforceHmac { get; set; }

    /// <summary>Number of events currently queued waiting for dispatch.</summary>
    public int PendingCount => (int)Interlocked.Read(ref _pendingCount);

    /// <summary>Total number of registered event subscriptions.</summary>
    public int SubscriberCount => _subscriberCount;

    /// <summary>Returns a snapshot of all EventBus metrics.</summary>
    public EventBusMetrics GetMetrics() => new()
    {
        PublishedCount = PublishedCount,
        DispatchedCount = DispatchedCount,
        DroppedCount = DroppedCount,
        ErrorCount = ErrorCount,
        UnsignedCount = UnsignedCount,
        ExpiredCount = ExpiredCount,
        DlqCount = DlqCount,
        StaleCount = StaleCount,
        CircuitBreakerTrips = CircuitBreakerTrips,
        DeduplicatedCount = DeduplicatedCount,
        ReplayedCount = ReplayedCount,
        QueuePressure = QueuePressure,
        PendingCount = PendingCount,
        SubscriberCount = SubscriberCount,
        SubscribedEventTypes = _handlers.Keys.ToList(),
    };

    public EventBus()
    {
        // WAL directory — created eagerly so WriteToWal() can start immediately
        _walDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "eventbus");
        _walPath = Path.Combine(_walDir, "wal.jsonl");
        try { Directory.CreateDirectory(_walDir); _walEnabled = true; }
        catch { _walEnabled = false; }

        _dispatchWorkers = new Thread[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            _dispatchWorkers[i] = new Thread(DispatchLoop)
            {
                IsBackground = true,
                Name = $"EventBus-Worker-{i}",
                Priority = ThreadPriority.BelowNormal
            };
            _dispatchWorkers[i].Start();
        }

        // Crash recovery: re-enqueue unprocessed events from previous run
        _ = Task.Run(RecoverUnprocessedEventsAsync);
        // Scheduled DLQ auto-replay with exponential backoff
        StartDlqReplayScheduler();

        SglLogger.Information("[EventBus] Started with {WorkerCount} worker threads, priority queue dispatch, capacity 50,000", WorkerCount);
    }

    /// <summary>
    /// Subscribe to events of a given type. Use "*" to subscribe to all events.
    /// </summary>
    public void Subscribe(string eventType, Func<TelemetryEvent, Task> handler)
    {
        var list = _handlers.GetOrAdd(eventType, _ => new List<Func<TelemetryEvent, Task>>());
        lock (list)
        {
            list.Add(handler);
        }
        Interlocked.Increment(ref _subscriberCount);
    }

    /// <summary>
    /// Publish an event. Non-blocking; events are enqueued for background dispatch.
    /// Returns false if the queue is full (event dropped).
    /// </summary>
    public bool Publish(TelemetryEvent evt)
    {
        if (_cts.IsCancellationRequested) return false;

        // v1.1.62: HMAC signature verification (warn-only or enforce mode)
        if (_signing != null)
        {
            if (string.IsNullOrEmpty(evt.Signature))
            {
                Interlocked.Increment(ref _unsignedCount);
                if (EnforceHmac)
                {
                    // v1.1.80: Throttle warning logs to prevent memory exhaustion.
                    // Only log every 1000th drop to avoid 27K+ warnings filling memory.
                    var count = Interlocked.Read(ref _unsignedCount);
                    if (count <= 5 || count % 1000 == 0)
                    {
                        SglLogger.Warning("[EventBus] Dropping unsigned events from {Source} (total dropped: {Count})",
                            evt.Source, count);
                    }
                    return false;
                }
            }
            else if (!_signing.VerifyEvent(evt, evt.Signature))
            {
                Interlocked.Increment(ref _errorCount);
                SglLogger.Warning("[EventBus] Invalid HMAC signature for event {EventType} from {Source}",
                    evt.EventType, evt.Source);
                return false; // DROP events with forged signatures
            }
        }

        // Event deduplication — lock-free via ConcurrentDictionary
        if (!_seenEventIds.TryAdd(evt.EventId, DateTime.UtcNow))
        {
            Interlocked.Increment(ref _deduplicatedCount);
            return true; // Already processed — treat as success
        }
        if (_seenEventIds.Count > DedupWindowSize)
            PruneDedupCache();

        // WAL: persist before enqueue for crash-recovery (graceful-degrades if I/O fails)
        WriteToWal(evt);

        // v1.1.73: Enqueue into priority queue instead of BlockingCollection
        bool enqueued;
        lock (_priorityQueue)
        {
            if (_priorityQueue.Count >= PriorityQueueCapacity)
            {
                enqueued = false;
            }
            else
            {
                _priorityQueue.Enqueue(evt, (int)evt.Priority);
                enqueued = true;
            }
        }

        if (enqueued)
        {
            _prioritySignal.Release();
            Interlocked.Increment(ref _publishedCount);
            Interlocked.Increment(ref _pendingCount);

            // Backpressure signaling: fire when queue exceeds 80% capacity
            if (QueuePressure > 0.8)
            {
                try { OnHighPressure?.Invoke(); } catch { /* fire-and-forget */ }
            }

            return true;
        }

        Interlocked.Increment(ref _droppedCount);
        WriteToDlq(evt);
        return false;
    }

    /// <summary>Convenience overload to publish with minimal ceremony.</summary>
    public bool Publish(string eventType, string source, string severity = "Info",
        Dictionary<string, object>? data = null, string? correlationId = null)
    {
        return Publish(new TelemetryEvent
        {
            EventType = eventType,
            Source = source,
            Severity = severity,
            Data = data ?? [],
            CorrelationId = correlationId
        });
    }

    /// <summary>
    /// v1.1.72: Async version of Publish. Awaits successful enqueue.
    /// Same HMAC and validation logic as Publish(), but returns a Task.
    /// </summary>
    public Task<bool> PublishAsync(TelemetryEvent evt)
    {
        if (_cts.IsCancellationRequested)
            return Task.FromResult(false);

        // v1.1.62: HMAC signature verification (warn-only or enforce mode)
        if (_signing != null)
        {
            if (string.IsNullOrEmpty(evt.Signature))
            {
                Interlocked.Increment(ref _unsignedCount);
                if (EnforceHmac)
                {
                    SglLogger.Warning("[EventBus] Dropping unsigned event {EventType} from {Source} (HMAC enforcement enabled)",
                        evt.EventType, evt.Source);
                    return Task.FromResult(false);
                }
            }
            else if (!_signing.VerifyEvent(evt, evt.Signature))
            {
                Interlocked.Increment(ref _errorCount);
                SglLogger.Warning("[EventBus] Invalid HMAC signature for event {EventType} from {Source}",
                    evt.EventType, evt.Source);
                return Task.FromResult(false); // DROP events with forged signatures
            }
        }

        // Event deduplication — lock-free via ConcurrentDictionary
        if (!_seenEventIds.TryAdd(evt.EventId, DateTime.UtcNow))
        {
            Interlocked.Increment(ref _deduplicatedCount);
            return Task.FromResult(true); // Already processed — treat as success
        }
        if (_seenEventIds.Count > DedupWindowSize)
            PruneDedupCache();

        // WAL: persist before enqueue for crash-recovery (graceful-degrades if I/O fails)
        WriteToWal(evt);

        // v1.1.73: Enqueue into priority queue instead of BlockingCollection
        bool enqueued;
        lock (_priorityQueue)
        {
            if (_priorityQueue.Count >= PriorityQueueCapacity)
            {
                enqueued = false;
            }
            else
            {
                _priorityQueue.Enqueue(evt, (int)evt.Priority);
                enqueued = true;
            }
        }

        if (enqueued)
        {
            _prioritySignal.Release();
            Interlocked.Increment(ref _publishedCount);
            Interlocked.Increment(ref _pendingCount);

            // Backpressure signaling: fire when queue exceeds 80% capacity
            if (QueuePressure > 0.8)
            {
                try { OnHighPressure?.Invoke(); } catch { /* fire-and-forget */ }
            }

            return Task.FromResult(true);
        }

        Interlocked.Increment(ref _droppedCount);
        WriteToDlq(evt);
        return Task.FromResult(false);
    }

    /// <summary>
    /// v1.1.72: Returns the number of subscribers registered for a specific topic.
    /// Use "*" to query wildcard subscriber count.
    /// </summary>
    public int GetTopicSubscribers(string topic)
    {
        if (_handlers.TryGetValue(topic, out var handlers))
        {
            lock (handlers) { return handlers.Count; }
        }
        return 0;
    }

    private void DispatchLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Wait for an item to be enqueued (or cancellation)
                _prioritySignal.Wait(_cts.Token);

                TelemetryEvent evt;
                lock (_priorityQueue)
                {
                    if (!_priorityQueue.TryDequeue(out evt!, out _))
                        continue;
                }
                Interlocked.Decrement(ref _pendingCount);

                try
                {
                    // TTL check: discard stale/expired events
                    var age = DateTime.UtcNow - evt.Timestamp;
                    var ttl = evt.GetEffectiveTtl();
                    if (age > ttl)
                    {
                        Interlocked.Increment(ref _expiredCount);
                        Interlocked.Increment(ref _staleCount);
                        continue;
                    }

                    var sw = Stopwatch.StartNew();

                    // v1.1.72: Log priority level for non-Normal events
                    if (evt.Priority < EventPriority.Normal)
                    {
                        SglLogger.Information("[EventBus] Dispatching {Priority} priority event: {EventType} from {Source}",
                            evt.Priority, evt.EventType, evt.Source);
                    }

                    DispatchEvent(evt).GetAwaiter().GetResult();
                    sw.Stop();
                    Interlocked.Increment(ref _dispatchedCount);
                    AckWalEvent(evt.EventId); // ACK: mark as processed in WAL

                    // Track per-event-type latency
                    _latencyStats.AddOrUpdate(
                        evt.EventType,
                        _ => (sw.ElapsedMilliseconds, 1),
                        (_, prev) => (prev.totalMs + sw.ElapsedMilliseconds, prev.count + 1));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _errorCount);
                    SglLogger.Warning("[EventBus] Dispatch error for {EventType}: {Message}",
                        evt.EventType, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task DispatchEvent(TelemetryEvent evt)
    {
        var tasks = new List<Task>();

        // Collect type-specific handlers
        if (_handlers.TryGetValue(evt.EventType, out var typeHandlers))
        {
            List<Func<TelemetryEvent, Task>> snapshot;
            lock (typeHandlers) { snapshot = [.. typeHandlers]; }
            foreach (var handler in snapshot)
            {
                var key = handler.GetHashCode();
                if (!IsHandlerPaused(key))
                    tasks.Add(SafeInvoke(handler, evt, key));
            }
        }

        // Collect wildcard handlers
        if (_handlers.TryGetValue("*", out var wildcardHandlers))
        {
            List<Func<TelemetryEvent, Task>> snapshot;
            lock (wildcardHandlers) { snapshot = [.. wildcardHandlers]; }
            foreach (var handler in snapshot)
            {
                var key = handler.GetHashCode();
                if (!IsHandlerPaused(key))
                    tasks.Add(SafeInvoke(handler, evt, key));
            }
        }

        // v1.1.56: Parallel fan-out, all handlers execute concurrently
        if (tasks.Count > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);

        // Durable event stream — append every event for replay capability (v1.1.91)
        if (_eventStream != null)
            _ = _eventStream.AppendAsync(evt);
    }

    /// <summary>Check if a handler is currently paused by the circuit breaker.</summary>
    private bool IsHandlerPaused(int handlerKey)
    {
        if (_handlerPausedUntil.TryGetValue(handlerKey, out var pausedUntil))
        {
            if (DateTime.UtcNow < pausedUntil)
                return true;

            // Pause expired: close circuit and resume the handler
            _handlerPausedUntil.TryRemove(handlerKey, out _);
            _handlerFailures.TryRemove(handlerKey, out _);
            SglLogger.Information("[EventBus] Circuit breaker CLOSED for handler {HandlerKey}, resuming dispatch", handlerKey);
        }
        return false;
    }

    private async Task SafeInvoke(Func<TelemetryEvent, Task> handler, TelemetryEvent evt, int handlerKey)
    {
        try
        {
            await handler(evt).ConfigureAwait(false);
            // Reset failure count on success
            _handlerFailures.TryRemove(handlerKey, out _);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            SglLogger.Warning("[EventBus] Handler failed for {EventType}: {Message}",
                evt.EventType, ex.Message);

            var failures = _handlerFailures.AddOrUpdate(handlerKey, 1, (_, prev) => prev + 1);
            if (failures >= CircuitBreakerThreshold)
            {
                _handlerPausedUntil[handlerKey] = DateTime.UtcNow + CircuitBreakerPause;
                Interlocked.Increment(ref _circuitBreakerTrips);
                SglLogger.Warning("[EventBus] Circuit breaker OPEN for handler {HandlerKey}, pausing 60s after {Failures} consecutive failures",
                    handlerKey, failures);
            }
        }
    }

    /// <summary>Persist a dropped event to the dead-letter queue as an AES-256 encrypted file.</summary>
    private void WriteToDlq(TelemetryEvent evt)
    {
        try
        {
            EnsureDlqDirectory();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff");
            var fileName = $"dlq_{timestamp}_{evt.EventType}.enc";
            // Sanitize filename: remove invalid path chars from EventType portion
            foreach (var c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            var filePath = Path.Combine(_dlqDir, fileName);
            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions { WriteIndented = true });

            // v1.1.74: Encrypt DLQ data at rest with AES-256-GCM (DPAPI-protected key)
            var encrypted = EncryptDlqData(json);

            lock (_dlqLock)
            {
                File.WriteAllBytes(filePath, encrypted);
            }
            Interlocked.Increment(ref _dlqCount);

            // Cap DLQ at 10,000 files: delete oldest when exceeded
            TrimDlqIfNeeded();
        }
        catch (Exception ex)
        {
            SglLogger.Error("[EventBus] Failed to write to DLQ", ex);
        }
    }

    /// <summary>Lazily create the DLQ directory.</summary>
    private void EnsureDlqDirectory()
    {
        if (_dlqDirCreated) return;
        lock (_dlqLock)
        {
            if (_dlqDirCreated) return;
            if (!Directory.Exists(_dlqDir))
                Directory.CreateDirectory(_dlqDir);
            _dlqDirCreated = true;
        }
    }

    /// <summary>Delete oldest DLQ files when exceeding the cap.</summary>
    private void TrimDlqIfNeeded()
    {
        try
        {
            var files = Directory.GetFiles(_dlqDir, "dlq_*.*");
            if (files.Length <= DlqMaxFiles) return;

            // Sort by name (timestamp-embedded) so oldest are first
            Array.Sort(files, StringComparer.Ordinal);
            var toDelete = files.Length - DlqMaxFiles;
            for (var i = 0; i < toDelete; i++)
            {
                try { File.Delete(files[i]); }
                catch { /* best effort cleanup */ }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[EventBus] Failed to trim DLQ directory", ex);
        }
    }

    /// <summary>Returns average dispatch latency (ms) per event type.</summary>
    public Dictionary<string, double> GetLatencyStats()
    {
        var result = new Dictionary<string, double>();
        foreach (var kvp in _latencyStats)
        {
            var (totalMs, evtCount) = kvp.Value;
            result[kvp.Key] = evtCount > 0 ? (double)totalMs / evtCount : 0;
        }
        return result;
    }

    /// <summary>
    /// Replay all dead-letter queue files. Decrypts each, deserializes, and re-publishes.
    /// Successfully replayed files are deleted. Returns the count of replayed events.
    /// </summary>
    public async Task<int> ReplayDlqAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_dlqDir))
            return 0;

        var files = Directory.GetFiles(_dlqDir, "*.enc");
        var replayed = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var encrypted = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
                var json = DecryptDlqData(encrypted);
                var evt = JsonSerializer.Deserialize<TelemetryEvent>(json);
                if (evt == null) continue;

                // Give the replayed event a fresh EventId so dedup doesn't reject it
                evt.EventId = Guid.NewGuid().ToString("N");

                if (Publish(evt))
                {
                    File.Delete(file);
                    replayed++;
                    Interlocked.Increment(ref _replayedCount);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                SglLogger.Warning("[EventBus] Failed to replay DLQ file {File}: {Message}", file, ex.Message);
            }
        }

        if (replayed > 0)
            SglLogger.Information("[EventBus] Replayed {Count} events from DLQ", replayed);

        return replayed;
    }

    /// <summary>v1.1.74: Encrypt plaintext JSON for DLQ storage using AES-256-GCM with DPAPI-protected key.</summary>
    private static byte[] EncryptDlqData(string plaintext)
    {
        var key = GetDlqEncryptionKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = new byte[12]; // AES-GCM standard nonce size
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16]; // AES-GCM standard tag size

        using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: [nonce (12)] [tag (16)] [ciphertext]
        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
        return result;
    }

    /// <summary>
    /// v1.1.74: Decrypt DLQ file back to JSON for replay.
    /// Tries AES-256-GCM first, falls back to legacy AES-256-CBC for backward compatibility.
    /// </summary>
    public static string DecryptDlqData(byte[] encrypted)
    {
        // Try AES-256-GCM first (new format: [nonce(12)][tag(16)][ciphertext])
        if (encrypted.Length >= 28)
        {
            try
            {
                var key = GetDlqEncryptionKey();
                var nonce = new byte[12];
                var tag = new byte[16];
                var ciphertext = new byte[encrypted.Length - 28];

                Buffer.BlockCopy(encrypted, 0, nonce, 0, 12);
                Buffer.BlockCopy(encrypted, 12, tag, 0, 16);
                Buffer.BlockCopy(encrypted, 28, ciphertext, 0, ciphertext.Length);

                var plaintext = new byte[ciphertext.Length];
                using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch
            {
                // GCM decryption failed — try legacy CBC format below
            }
        }

        // Fallback: legacy AES-256-CBC format [IV(16)][ciphertext] with deterministic key
        return DecryptDlqDataLegacyCbc(encrypted);
    }

    /// <summary>Legacy AES-256-CBC decryption for DLQ files created before v1.1.74.</summary>
    private static string DecryptDlqDataLegacyCbc(byte[] encrypted)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = DeriveOldEncryptionKey();

        // Extract IV from first 16 bytes
        var iv = new byte[16];
        Array.Copy(encrypted, 0, iv, 0, 16);
        aes.IV = iv;

        using var ms = new MemoryStream(encrypted, 16, encrypted.Length - 16);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var reader = new StreamReader(cs, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// v1.1.74: Get or create a DPAPI-protected AES-256 key for DLQ encryption.
    /// On first use: generates a random 32-byte key, DPAPI-protects it, saves to data/security/dlq.key.dpapi.
    /// On subsequent use: reads the DPAPI-protected file and unprotects it.
    /// Falls back to the legacy deterministic key if DPAPI is unavailable (e.g. Linux).
    /// </summary>
    private static byte[] GetDlqEncryptionKey()
    {
        if (_dlqEncryptionKey != null) return _dlqEncryptionKey;
        lock (_dlqKeyLock)
        {
            if (_dlqEncryptionKey != null) return _dlqEncryptionKey;
            _dlqEncryptionKey = LoadOrCreateDpapiKey();
            return _dlqEncryptionKey;
        }
    }

    /// <summary>Load existing DPAPI key or generate a new one.</summary>
    private static byte[] LoadOrCreateDpapiKey()
    {
        try
        {
            var keyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "security");
            var keyPath = Path.Combine(keyDir, "dlq.key.dpapi");

            if (File.Exists(keyPath))
            {
                var protectedKey = File.ReadAllBytes(keyPath);
                return System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedKey, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }

            // Generate new random 32-byte key
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            // DPAPI-protect and persist
            Directory.CreateDirectory(keyDir);
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                key, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyPath, encrypted);

            SglLogger.Information("[EventBus] Created new DPAPI-protected DLQ encryption key at {Path}", keyPath);
            return key;
        }
        catch (Exception ex)
        {
            // Fallback to legacy deterministic key if DPAPI is unavailable
            SglLogger.Warning("[EventBus] DPAPI key load/create failed, falling back to legacy key derivation: {Message}", ex.Message);
            return DeriveOldEncryptionKey();
        }
    }

    /// <summary>Legacy deterministic key derivation for backward compatibility with pre-v1.1.74 DLQ files.</summary>
    private static byte[] DeriveOldEncryptionKey()
    {
        var seed = $"MCOfflineChat-DLQ-{Environment.MachineName}-{AppDomain.CurrentDomain.BaseDirectory}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    // =========================================================================
    // WAL persistence + Crash Recovery + DLQ Auto-Replay
    // =========================================================================

    /// <summary>
    /// Prune LRU dedup cache: remove entries older than 5 minutes (best-effort, lock-free).
    /// Called when the ConcurrentDictionary exceeds DedupWindowSize.
    /// </summary>
    private void PruneDedupCache()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-300);
        foreach (var kvp in _seenEventIds)
        {
            if (kvp.Value < cutoff)
                _seenEventIds.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Append a WAL "write" record to data/eventbus/wal.jsonl before the event is enqueued.
    /// No-op (and self-disabling) if I/O fails, so the caller is never blocked.
    /// </summary>
    private void WriteToWal(TelemetryEvent evt)
    {
        if (!_walEnabled) return;
        try
        {
            var record = JsonSerializer.Serialize(new
            {
                Action = "write",
                evt.EventId,
                evt.Timestamp,
                Event = evt
            });
            lock (_walLock)
                File.AppendAllText(_walPath, record + "\n");
        }
        catch (Exception ex)
        {
            // Graceful degradation: disable WAL rather than blocking high-frequency publishers
            _walEnabled = false;
            SglLogger.Warning("[EventBus] WAL write failed — disabling WAL persistence: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Append a WAL "ack" record after an event is successfully dispatched.
    /// No-op if WAL is disabled; ACK failures are non-critical (worst case: event replayed on next restart).
    /// </summary>
    private void AckWalEvent(string eventId)
    {
        if (!_walEnabled) return;
        try
        {
            var record = JsonSerializer.Serialize(new
            {
                Action = "ack",
                EventId = eventId,
                Timestamp = DateTime.UtcNow
            });
            lock (_walLock)
                File.AppendAllText(_walPath, record + "\n");
        }
        catch
        {
            // Best-effort: lost ACK just means the event might be replayed on next startup
        }
    }

    /// <summary>
    /// Crash recovery: atomically renames the WAL file, re-enqueues every event that was
    /// written but never ACK'd (i.e. the process crashed before dispatch), then deletes
    /// the recovery file. New WAL writes during recovery go to a fresh wal.jsonl.
    /// Called from the constructor via Task.Run so startup is not blocked.
    /// </summary>
    public async Task RecoverUnprocessedEventsAsync()
    {
        if (!_walEnabled) return;
        try
        {
            var recoveryPath = _walPath + ".recovery";

            // Atomic rename so new WAL writes start a fresh file immediately
            lock (_walLock)
            {
                if (!File.Exists(_walPath)) return;
                File.Move(_walPath, recoveryPath, overwrite: true);
            }

            var pending = new Dictionary<string, TelemetryEvent>();
            var acked  = new HashSet<string>();

            foreach (var line in await File.ReadAllLinesAsync(recoveryPath).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc  = JsonDocument.Parse(line);
                    var root       = doc.RootElement;
                    var action     = root.GetProperty("Action").GetString();
                    var eventId    = root.GetProperty("EventId").GetString() ?? string.Empty;

                    if (action == "write")
                    {
                        var evt = root.GetProperty("Event").Deserialize<TelemetryEvent>();
                        if (evt != null) pending[eventId] = evt;
                    }
                    else if (action == "ack")
                    {
                        acked.Add(eventId);
                        pending.Remove(eventId);
                    }
                }
                catch { /* skip malformed WAL lines */ }
            }

            // Re-publish events that were never ACK'd (fresh ID bypasses dedup cache)
            var recovered = 0;
            foreach (var evt in pending.Values)
            {
                if (!acked.Contains(evt.EventId))
                {
                    evt.EventId = Guid.NewGuid().ToString("N");
                    if (Publish(evt)) recovered++;
                }
            }

            try { File.Delete(recoveryPath); } catch { /* best effort */ }

            SglLogger.Information("[EventBus] WAL recovery complete: re-enqueued {Count} unprocessed events", recovered);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[EventBus] WAL recovery failed, running in-memory only: {Message}", ex.Message);
            _walEnabled = false;
        }
    }

    /// <summary>
    /// Start a background Timer that fires every 60 seconds to replay DLQ events with
    /// per-file exponential backoff. Events that exceed 5 retries are permanently failed.
    /// </summary>
    public void StartDlqReplayScheduler()
    {
        _dlqReplayTimer = new Timer(
            _ =>
            {
                try { ReplayDlqScheduledAsync().GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    SglLogger.Warning("[EventBus] DLQ scheduler error: {Message}", ex.Message);
                }
            },
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Attempt to replay each DLQ .enc file, honouring exponential backoff per file.
    /// Backoff delay = 2^retryCount seconds, capped at 300 s.
    /// Files where RetryCount > 5 are renamed to .permanent_failure and skipped forever.
    /// </summary>
    private async Task ReplayDlqScheduledAsync()
    {
        if (!Directory.Exists(_dlqDir)) return;

        var files = Directory.GetFiles(_dlqDir, "*.enc");
        if (files.Length == 0) return;

        int replayed = 0, permanentFailures = 0;

        foreach (var file in files)
        {
            try
            {
                var fileKey = Path.GetFileName(file);
                var meta    = _dlqRetryMeta.GetOrAdd(fileKey, _ => (0, DateTime.MinValue));
                var (retryCount, lastAttempt) = meta;

                // Permanent failure: exceeded 5 retries
                if (retryCount > 5)
                {
                    var failPath = file + ".permanent_failure";
                    if (!File.Exists(failPath))
                        try { File.Move(file, failPath); } catch { /* best effort */ }
                    _dlqRetryMeta.TryRemove(fileKey, out _);
                    permanentFailures++;
                    continue;
                }

                // Exponential backoff: 2^retryCount seconds, capped at 300 s
                var backoffSec = Math.Min(Math.Pow(2, retryCount), 300);
                if (DateTime.UtcNow - lastAttempt < TimeSpan.FromSeconds(backoffSec))
                    continue; // Not yet time to retry

                // Record attempt timestamp before trying (ensures retry count advances even on crash)
                _dlqRetryMeta[fileKey] = (retryCount, DateTime.UtcNow);

                var encrypted = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                var json      = DecryptDlqData(encrypted);
                var evt       = JsonSerializer.Deserialize<TelemetryEvent>(json);
                if (evt == null) continue;

                evt.EventId = Guid.NewGuid().ToString("N"); // fresh ID bypasses dedup cache

                if (Publish(evt))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                    _dlqRetryMeta.TryRemove(fileKey, out _);
                    replayed++;
                    Interlocked.Increment(ref _replayedCount);
                }
                else
                {
                    // Queue still full — bump retry counter for next cycle
                    _dlqRetryMeta[fileKey] = (retryCount + 1, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                var fileKey = Path.GetFileName(file);
                _dlqRetryMeta.AddOrUpdate(
                    fileKey,
                    _ => (1, DateTime.UtcNow),
                    (_, m) => (m.RetryCount + 1, DateTime.UtcNow));
                SglLogger.Warning("[EventBus] DLQ scheduled replay error for {File}: {Message}", file, ex.Message);
            }
        }

        if (replayed > 0 || permanentFailures > 0)
            SglLogger.Information("[EventBus] DLQ auto-replay: replayed={Replayed}, permanentFailures={PF}",
                replayed, permanentFailures);
    }

    public void Dispose()
    {
        _dlqReplayTimer?.Dispose();
        _cts.Cancel();
        // Unblock all worker threads waiting on the semaphore
        for (int i = 0; i < WorkerCount; i++)
            _prioritySignal.Release();
        foreach (var worker in _dispatchWorkers)
            worker.Join(timeout: TimeSpan.FromSeconds(3));
        _prioritySignal.Dispose();
        _cts.Dispose();
        SglLogger.Information("[EventBus] Shut down. Published={Published}, Dispatched={Dispatched}, Dropped={Dropped}, Stale={Stale}, DLQ={Dlq}, Deduped={Deduped}, Replayed={Replayed}, CBTrips={CBTrips}, Errors={Errors}",
            PublishedCount, DispatchedCount, DroppedCount, StaleCount, DlqCount, DeduplicatedCount, ReplayedCount, CircuitBreakerTrips, ErrorCount);
    }
}

/// <summary>Snapshot DTO of EventBus metrics for admin console / API.</summary>
public sealed class EventBusMetrics
{
    public long PublishedCount { get; init; }
    public long DispatchedCount { get; init; }
    public long DroppedCount { get; init; }
    public long ErrorCount { get; init; }
    public long UnsignedCount { get; init; }
    public long ExpiredCount { get; init; }
    public long DlqCount { get; init; }
    public long StaleCount { get; init; }
    public long CircuitBreakerTrips { get; init; }
    public long DeduplicatedCount { get; init; }
    public long ReplayedCount { get; init; }
    public double QueuePressure { get; init; }
    public int PendingCount { get; init; }
    public int SubscriberCount { get; init; }
    public List<string> SubscribedEventTypes { get; init; } = [];
}
