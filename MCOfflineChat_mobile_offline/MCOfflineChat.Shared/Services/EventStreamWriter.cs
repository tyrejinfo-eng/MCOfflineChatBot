using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Durable append-only event stream. Every published TelemetryEvent is written to a JSONL file.
/// Supports full replay for forensics, SOC investigation, and ML training.
/// v1.1.91: deterministic replay with `await foreach(var evt in stream.Replay()) { ... }`
/// </summary>
public sealed class EventStreamWriter : IDisposable
{
    private readonly string _streamDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _totalEvents;
    private long _totalBytes;

    public long TotalEvents => _totalEvents;
    public long TotalBytes => _totalBytes;
    public string StreamDirectory => _streamDirectory;

    public EventStreamWriter(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? Path.Combine(AppContext.BaseDirectory, "data", "eventstream");
        Directory.CreateDirectory(dir);
        _streamDirectory = dir;
    }

    /// <summary>Appends a TelemetryEvent to today's stream file (non-blocking, best-effort).</summary>
    public async Task AppendAsync(TelemetryEvent evt, CancellationToken ct = default)
    {
        try
        {
            var line = JsonSerializer.Serialize(evt) + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);

            await _writeLock.WaitAsync(ct);
            try
            {
                var filePath = GetTodayFilePath();
                await File.AppendAllTextAsync(filePath, line, ct);
                Interlocked.Increment(ref _totalEvents);
                Interlocked.Add(ref _totalBytes, bytes.Length);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch
        {
            // Best-effort — never crash the caller
        }
    }

    /// <summary>Replays ALL events from the stream (all files, oldest first). Thread-safe read.</summary>
    public async IAsyncEnumerable<TelemetryEvent> Replay(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_streamDirectory, "events_*.jsonl")
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            await foreach (var evt in ReplayFileAsync(file, ct))
                yield return evt;
        }
    }

    /// <summary>Replays events from a specific date forward.</summary>
    public async IAsyncEnumerable<TelemetryEvent> ReplayFromAsync(
        DateTime from,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_streamDirectory, "events_*.jsonl")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Length >= 17 && DateTime.TryParseExact(
                    name[7..], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                    return fileDate >= from.Date;
                return true;
            })
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            await foreach (var evt in ReplayFileAsync(file, ct))
            {
                if (evt.Timestamp >= from)
                    yield return evt;
            }
        }
    }

    /// <summary>Replays events matching a specific topic pattern.</summary>
    public async IAsyncEnumerable<TelemetryEvent> ReplayTopicAsync(
        string topicPattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in Replay(ct))
        {
            if (topicPattern == "*" || evt.EventType?.StartsWith(topicPattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase) == true)
                yield return evt;
        }
    }

    /// <summary>Returns stream statistics per day.</summary>
    public IReadOnlyList<StreamDaySummary> GetSummary()
    {
        var summaries = new List<StreamDaySummary>();
        foreach (var file in Directory.GetFiles(_streamDirectory, "events_*.jsonl").OrderBy(f => f))
        {
            var info = new FileInfo(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var dateStr = name.Length >= 17 ? name[7..] : name;
            summaries.Add(new StreamDaySummary
            {
                Date = dateStr,
                FilePath = file,
                FileSizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc
            });
        }
        return summaries;
    }

    private string GetTodayFilePath()
        => Path.Combine(_streamDirectory, $"events_{DateTime.UtcNow:yyyy-MM-dd}.jsonl");

    private static async IAsyncEnumerable<TelemetryEvent> ReplayFileAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) yield break;

        string[] lines;
        try { lines = await File.ReadAllLinesAsync(filePath, ct); }
        catch { yield break; }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            TelemetryEvent? evt = null;
            try { evt = JsonSerializer.Deserialize<TelemetryEvent>(line); } catch { }
            if (evt != null) yield return evt;
        }
    }

    public void Dispose() => _writeLock.Dispose();
}

public sealed class StreamDaySummary
{
    public string Date { get; init; } = "";
    public string FilePath { get; init; } = "";
    public long FileSizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
}
