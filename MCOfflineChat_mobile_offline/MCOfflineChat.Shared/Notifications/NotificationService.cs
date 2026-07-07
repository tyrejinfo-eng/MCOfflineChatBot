using System.Collections.Concurrent;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Notifications;

public enum NotificationSeverity
{
    Info,       // Routine informational
    Notice,     // Noteworthy but non-urgent
    Warning,    // Potential issues requiring attention
    Alert,      // Security events needing review
    Critical,   // Active threats or system failures
    Emergency   // Requires immediate action
}

public sealed class Notification
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public NotificationSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Central notification service with severity-based routing, deduplication,
/// rate limiting, persistence, and offline delivery queues.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly ConcurrentQueue<Notification> _history = new();
    private readonly ConcurrentDictionary<string, DateTime> _dedup = new();
    private readonly ConcurrentDictionary<string, (int count, DateTime windowStart)> _rateLimits = new();
    private readonly ConcurrentQueue<Notification> _offlineQueue = new();
    private readonly List<Action<Notification>> _listeners = [];
    private readonly object _listenerLock = new();
    private readonly string _persistPath;
    private readonly Timer _persistTimer;
    private readonly Timer _cleanupTimer;

    // Configuration
    private const int MaxHistory = 500;
    private const int DeduplicationWindowSeconds = 60;
    private const int RateLimitPerMinute = 30;
    private const int MaxOfflineQueue = 200;

    private long _totalSent;
    private long _totalDeduplicated;
    private long _totalRateLimited;
    private bool _isOnline = true;

    public long TotalSent => Interlocked.Read(ref _totalSent);
    public long TotalDeduplicated => Interlocked.Read(ref _totalDeduplicated);
    public long TotalRateLimited => Interlocked.Read(ref _totalRateLimited);
    public int QueuedCount => _history.Count;
    public int OfflineQueueCount => _offlineQueue.Count;
    public int UnreadCount => _history.Count(n => !n.IsRead && !n.IsDismissed);

    public NotificationService(string? persistDirectory = null)
    {
        var dir = persistDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCOfflineChat", "notifications");
        Directory.CreateDirectory(dir);
        _persistPath = Path.Combine(dir, "notifications.json");

        LoadPersistedNotifications();

        // Persist every 60 seconds
        _persistTimer = new Timer(_ => PersistNotifications(), null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        // Clean expired dedup entries every 30 seconds
        _cleanupTimer = new Timer(_ => CleanupDedup(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        SglLogger.Information("[NotificationService] Initialized. Loaded {Count} persisted notifications", _history.Count);
    }

    /// <summary>Register a listener to receive new notifications (UI, WebSocket push, etc.).</summary>
    public void OnNotification(Action<Notification> listener)
    {
        lock (_listenerLock) _listeners.Add(listener);
    }

    /// <summary>Remove a registered listener.</summary>
    public void RemoveListener(Action<Notification> listener)
    {
        lock (_listenerLock) _listeners.Remove(listener);
    }

    /// <summary>Send a notification. Returns false if deduplicated or rate-limited.</summary>
    public bool Notify(Notification notification)
    {
        // Deduplication: same title+source within window
        var dedupKey = $"{notification.Source}:{notification.Title}";
        if (_dedup.TryGetValue(dedupKey, out var lastTime) &&
            (DateTime.UtcNow - lastTime).TotalSeconds < DeduplicationWindowSeconds)
        {
            Interlocked.Increment(ref _totalDeduplicated);
            return false;
        }
        _dedup[dedupKey] = DateTime.UtcNow;

        // Rate limiting per source
        var rateKey = notification.Source;
        if (_rateLimits.TryGetValue(rateKey, out var rate))
        {
            if ((DateTime.UtcNow - rate.windowStart).TotalMinutes < 1)
            {
                if (rate.count >= RateLimitPerMinute &&
                    notification.Severity < NotificationSeverity.Critical) // Critical/Emergency bypass rate limit
                {
                    Interlocked.Increment(ref _totalRateLimited);
                    return false;
                }
                _rateLimits[rateKey] = (rate.count + 1, rate.windowStart);
            }
            else
            {
                _rateLimits[rateKey] = (1, DateTime.UtcNow);
            }
        }
        else
        {
            _rateLimits[rateKey] = (1, DateTime.UtcNow);
        }

        // Add to history
        _history.Enqueue(notification);
        while (_history.Count > MaxHistory) _history.TryDequeue(out _);

        Interlocked.Increment(ref _totalSent);

        // Deliver
        if (_isOnline)
        {
            DeliverToListeners(notification);
            FlushOfflineQueue();
        }
        else
        {
            _offlineQueue.Enqueue(notification);
            while (_offlineQueue.Count > MaxOfflineQueue) _offlineQueue.TryDequeue(out _);
        }

        return true;
    }

    /// <summary>Convenience overload.</summary>
    public bool Notify(NotificationSeverity severity, string title, string message,
        string source = "System", string? correlationId = null)
    {
        return Notify(new Notification
        {
            Severity = severity,
            Title = title,
            Message = message,
            Source = source,
            CorrelationId = correlationId
        });
    }

    /// <summary>Mark the service as online, flushing offline queue.</summary>
    public void SetOnline()
    {
        _isOnline = true;
        FlushOfflineQueue();
    }

    /// <summary>Mark the service as offline — notifications queue for later delivery.</summary>
    public void SetOffline() => _isOnline = false;

    /// <summary>Get notification history, newest first.</summary>
    public IReadOnlyList<Notification> GetHistory(int limit = 50, NotificationSeverity? minSeverity = null)
    {
        var query = _history.AsEnumerable();
        if (minSeverity.HasValue)
            query = query.Where(n => n.Severity >= minSeverity.Value);
        return query.OrderByDescending(n => n.Timestamp).Take(limit).ToList();
    }

    /// <summary>Mark a notification as read.</summary>
    public void MarkRead(string notificationId)
    {
        var n = _history.FirstOrDefault(x => x.Id == notificationId);
        if (n != null) n.IsRead = true;
    }

    /// <summary>Mark all as read.</summary>
    public void MarkAllRead()
    {
        foreach (var n in _history) n.IsRead = true;
    }

    /// <summary>Dismiss a notification.</summary>
    public void Dismiss(string notificationId)
    {
        var n = _history.FirstOrDefault(x => x.Id == notificationId);
        if (n != null) n.IsDismissed = true;
    }

    /// <summary>Wire up to an EventBus to auto-generate notifications from telemetry events.</summary>
    public void SubscribeToEventBus(EventBus eventBus)
    {
        eventBus.Subscribe("threat.detected", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Alert, "Threat Detected",
                evt.Get<string>("description") ?? $"Threat from {evt.Source}",
                evt.Source, evt.CorrelationId);
        }));

        eventBus.Subscribe("threat.critical", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Critical, "Critical Threat",
                evt.Get<string>("description") ?? $"Critical threat from {evt.Source}",
                evt.Source, evt.CorrelationId);
        }));

        eventBus.Subscribe("scan.complete", evt => Task.Run(() =>
        {
            var threats = evt.Get<int>("threatCount");
            Notify(threats > 0 ? NotificationSeverity.Warning : NotificationSeverity.Info,
                "Scan Complete",
                threats > 0 ? $"Found {threats} threat(s) during scan" : "Scan completed — no threats found",
                evt.Source, evt.CorrelationId);
        }));

        eventBus.Subscribe("system.error", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Warning, "System Error",
                evt.Get<string>("message") ?? "A system error occurred",
                evt.Source, evt.CorrelationId);
        }));

        eventBus.Subscribe("swarm.node.lost", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Alert, "Swarm Node Lost",
                evt.Get<string>("nodeId") ?? "A swarm node disconnected unexpectedly",
                "Swarm", evt.CorrelationId);
        }));

        eventBus.Subscribe("compute.task.failed", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Warning, "Compute Task Failed",
                evt.Get<string>("reason") ?? "A distributed compute task failed",
                "Compute", evt.CorrelationId);
        }));

        eventBus.Subscribe("breach.found", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Alert, "Breach Detected",
                evt.Get<string>("details") ?? "New breach data found for monitored targets",
                "DarkWebMonitor", evt.CorrelationId);
        }));

        eventBus.Subscribe("selfhealing.action", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Notice, "Self-Healing Action",
                evt.Get<string>("action") ?? "Automatic remediation applied",
                "SelfHealing", evt.CorrelationId);
        }));

        eventBus.Subscribe("admin.alert", evt => Task.Run(() =>
        {
            Notify(NotificationSeverity.Alert, "Admin Alert",
                evt.Get<string>("message") ?? "Administrative action required",
                "Admin", evt.CorrelationId);
        }));

        SglLogger.Information("[NotificationService] Subscribed to EventBus telemetry channels");
    }

    private void DeliverToListeners(Notification notification)
    {
        List<Action<Notification>> snapshot;
        lock (_listenerLock) { snapshot = [.. _listeners]; }

        foreach (var listener in snapshot)
        {
            try
            {
                listener(notification);
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[NotificationService] Listener error: {Message}", ex.Message);
            }
        }
    }

    private void FlushOfflineQueue()
    {
        while (_offlineQueue.TryDequeue(out var queued))
        {
            DeliverToListeners(queued);
        }
    }

    private void CleanupDedup()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-DeduplicationWindowSeconds * 2);
        foreach (var key in _dedup.Keys.ToList())
        {
            if (_dedup.TryGetValue(key, out var ts) && ts < cutoff)
                _dedup.TryRemove(key, out _);
        }
    }

    private void PersistNotifications()
    {
        try
        {
            var recent = _history.OrderByDescending(n => n.Timestamp).Take(100).ToList();
            var json = JsonSerializer.Serialize(recent, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(_persistPath, json);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[NotificationService] Persist failed: {Message}", ex.Message);
        }
    }

    private void LoadPersistedNotifications()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;
            var json = File.ReadAllText(_persistPath);
            var items = JsonSerializer.Deserialize<List<Notification>>(json);
            if (items == null) return;
            foreach (var item in items.OrderBy(n => n.Timestamp))
                _history.Enqueue(item);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[NotificationService] Load failed: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        _persistTimer.Dispose();
        _cleanupTimer.Dispose();
        PersistNotifications();
    }
}
