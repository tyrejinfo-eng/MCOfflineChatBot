using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Three-tier storage router for security events. Routes incoming telemetry to:
///   Hot Layer  → in-memory LRU cache for real-time dashboards (last 5 minutes)
///   Index Layer → in-memory reverse index for fast investigation queries
///   Cold Layer  → HMAC-signed JSONL append files for tamper-evident archival
/// </summary>
public sealed class StorageRouter : IDisposable
{
    private readonly string _archivePath;
    private readonly byte[] _hmacKey;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Hot tier: circular buffer of recent events (last ~10K events)
    private readonly ConcurrentQueue<TelemetryEvent> _hotBuffer = new();
    private const int HotBufferMaxSize = 10_000;

    // Index tier: reverse index by event type → file offsets
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> _eventTypeIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> _sourceIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> _severityIndex = new();

    // Metrics
    private long _writtenCount;
    private long _hotHits;
    private long _indexHits;
    private long _archiveHits;

    public long WrittenCount => Interlocked.Read(ref _writtenCount);
    public long HotHits => Interlocked.Read(ref _hotHits);
    public long IndexHits => Interlocked.Read(ref _indexHits);
    public long ArchiveHits => Interlocked.Read(ref _archiveHits);
    public int HotBufferCount => _hotBuffer.Count;

    public StorageRouter(string dataDir, byte[]? hmacKey = null)
    {
        _archivePath = Path.Combine(dataDir, "events");
        Directory.CreateDirectory(_archivePath);

        // Use provided key (from SecretManager) or generate a random ephemeral key as fallback
        _hmacKey = hmacKey ?? SecretManager.GetOrCreateHmacKey(dataDir);

        SglLogger.Information("[StorageRouter] Initialized. Archive: {Path}", _archivePath);
    }

    /// <summary>
    /// Store an event through all three tiers.
    /// </summary>
    public async Task StoreAsync(TelemetryEvent evt)
    {
        // 1. Hot tier: push to circular buffer
        _hotBuffer.Enqueue(evt);
        while (_hotBuffer.Count > HotBufferMaxSize)
            _hotBuffer.TryDequeue(out _);

        // 2. Index tier: update reverse indexes
        var offset = Interlocked.Read(ref _writtenCount);
        _eventTypeIndex.GetOrAdd(evt.EventType, _ => []).Add(offset);
        if (!string.IsNullOrEmpty(evt.Source))
            _sourceIndex.GetOrAdd(evt.Source, _ => []).Add(offset);
        _severityIndex.GetOrAdd(evt.Severity, _ => []).Add(offset);

        // 3. Cold tier: HMAC-signed JSONL append
        await WriteToArchiveAsync(evt);

        Interlocked.Increment(ref _writtenCount);
    }

    /// <summary>
    /// Query hot tier for recent events (fastest, last 5 minutes typically).
    /// </summary>
    public IReadOnlyList<TelemetryEvent> QueryHot(
        string? eventType = null,
        string? source = null,
        string? severity = null,
        int limit = 100)
    {
        Interlocked.Increment(ref _hotHits);
        IEnumerable<TelemetryEvent> query = _hotBuffer;

        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(e => e.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(source))
            query = query.Where(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(severity))
            query = query.Where(e => e.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));

        return query.OrderByDescending(e => e.Timestamp).Take(limit).ToList();
    }

    /// <summary>
    /// Query index tier for event counts by type (fast aggregation).
    /// </summary>
    public IReadOnlyDictionary<string, int> GetEventTypeCounts()
    {
        Interlocked.Increment(ref _indexHits);
        return _eventTypeIndex.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    /// <summary>
    /// Query index tier for events from a specific source.
    /// </summary>
    public int GetCountBySource(string source)
    {
        Interlocked.Increment(ref _indexHits);
        return _sourceIndex.TryGetValue(source, out var offsets) ? offsets.Count : 0;
    }

    /// <summary>
    /// Get archive file paths for a date range (for cold-tier queries).
    /// </summary>
    public IReadOnlyList<string> GetArchiveFiles(DateTime from, DateTime to)
    {
        Interlocked.Increment(ref _archiveHits);
        var files = new List<string>();
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            var file = Path.Combine(_archivePath, $"events_{date:yyyy-MM-dd}.jsonl");
            if (File.Exists(file))
                files.Add(file);
        }
        return files;
    }

    private async Task WriteToArchiveAsync(TelemetryEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOpts);
        var encrypted = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // CRITICAL: Create a per-call HMAC instance. HMACSHA256 is NOT thread-safe —
        // sharing a single instance across EventBus worker threads causes
        // AccessViolationException in BCryptFinishHash (native memory corruption)
        // that instantly kills the process with no possibility of catch.
        string hmacHex;
        using (var hmac = new HMACSHA256(_hmacKey))
        {
            var hmacBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(encrypted));
            hmacHex = Convert.ToHexString(hmacBytes);
        }

        var line = $"{evt.Timestamp:o}|{encrypted}|{hmacHex}\n";
        var filePath = Path.Combine(_archivePath, $"events_{DateTime.UtcNow:yyyy-MM-dd}.jsonl");

        // Safety: ensure archive directory still exists
        Directory.CreateDirectory(_archivePath);

        await _writeLock.WaitAsync();
        try
        {
            // Use FileStream with FileShare.ReadWrite to avoid locking out other processes
            using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = Encoding.UTF8.GetBytes(line);
            await fs.WriteAsync(bytes);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
