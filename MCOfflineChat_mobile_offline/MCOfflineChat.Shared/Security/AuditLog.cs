// MCOfflineChat.Shared - Tamper-Evident Audit Log
// Hash-chained append-only audit log for security events
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Tamper-evident, hash-chained audit log. Each entry includes the SHA-256 hash
/// of the previous entry, forming an append-only integrity chain. Thread-safe
/// via SemaphoreSlim. Writes to data/audit/audit_log.jsonl in JSONL format.
/// </summary>
public sealed class AuditLog : IDisposable
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string _lastHash = "GENESIS"; // Initial chain seed
    private readonly List<AuditEntry> _recentEntries = new();
    private const int MaxRecentEntries = 10000;
    private bool _disposed;

    // Well-known event types
    public static class EventTypes
    {
        public const string AdminLogin = "admin.login";
        public const string AdminLogout = "admin.logout";
        public const string AdminFailedLogin = "admin.failed_login";
        public const string EngineStart = "engine.start";
        public const string EngineStop = "engine.stop";
        public const string UserCreate = "user.create";
        public const string UserDelete = "user.delete";
        public const string ConfigChange = "config.change";
        public const string AlertAcknowledge = "alert.acknowledge";
        public const string EndpointIsolate = "endpoint.isolate";
        public const string ModelMount = "model.mount";
    }

    public AuditLog(string dataDirectory = "data")
    {
        var auditDir = Path.Combine(dataDirectory, "audit");
        Directory.CreateDirectory(auditDir);
        _logFilePath = Path.Combine(auditDir, "audit_log.jsonl");

        LoadExistingChain();
    }

    /// <summary>
    /// Appends a new event to the audit log with hash chaining.
    /// </summary>
    public void LogEvent(string eventType, string user, string details, string? ipAddress = null)
    {
        if (_disposed) return;

        _lock.Wait();
        try
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.UtcNow.ToString("O"),
                EventType = eventType,
                User = user,
                Details = details,
                IpAddress = ipAddress ?? "local",
                PreviousHash = _lastHash
            };

            // Compute hash of this entry (excluding its own hash field)
            entry.Hash = ComputeEntryHash(entry);
            _lastHash = entry.Hash;

            // Write to JSONL file
            var json = JsonSerializer.Serialize(entry, _jsonOptions);

            try
            {
                File.AppendAllText(_logFilePath, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                SglLogger.Error("[AuditLog] Failed to write audit entry", ex);
            }

            // Keep in memory for fast recent queries
            _recentEntries.Add(entry);
            if (_recentEntries.Count > MaxRecentEntries)
            {
                _recentEntries.RemoveRange(0, _recentEntries.Count - MaxRecentEntries);
            }

            SglLogger.Information("[AuditLog] {EventType} by {User}: {Details}",
                eventType, user, TruncateForLog(details));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns the most recent N audit events.
    /// </summary>
    public List<AuditEntry> GetRecentEvents(int count)
    {
        _lock.Wait();
        try
        {
            var startIndex = Math.Max(0, _recentEntries.Count - count);
            var length = Math.Min(count, _recentEntries.Count);
            return _recentEntries.GetRange(startIndex, length).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Verifies the integrity of the hash chain. Returns (isValid, brokenAtIndex).
    /// If valid, brokenAtIndex is -1. If broken, brokenAtIndex is the index of the
    /// first entry whose chain link is invalid.
    /// </summary>
    public (bool IsValid, int BrokenAtIndex) VerifyIntegrity()
    {
        _lock.Wait();
        try
        {
            if (_recentEntries.Count == 0)
                return (true, -1);

            // Load full chain from disk for thorough verification
            var allEntries = LoadAllEntries();

            if (allEntries.Count == 0)
                return (true, -1);

            // First entry should chain from GENESIS
            if (allEntries[0].PreviousHash != "GENESIS")
            {
                SglLogger.Warning("[AuditLog] Integrity check: first entry does not chain from GENESIS");
                return (false, 0);
            }

            // Verify each entry's hash
            var recomputedHash = ComputeEntryHash(allEntries[0]);
            if (recomputedHash != allEntries[0].Hash)
            {
                SglLogger.Warning("[AuditLog] Integrity check: entry 0 hash mismatch");
                return (false, 0);
            }

            for (int i = 1; i < allEntries.Count; i++)
            {
                // Check chain link
                if (allEntries[i].PreviousHash != allEntries[i - 1].Hash)
                {
                    SglLogger.Warning("[AuditLog] Integrity check: chain broken at entry {Index}", i);
                    return (false, i);
                }

                // Verify entry hash
                recomputedHash = ComputeEntryHash(allEntries[i]);
                if (recomputedHash != allEntries[i].Hash)
                {
                    SglLogger.Warning("[AuditLog] Integrity check: hash mismatch at entry {Index}", i);
                    return (false, i);
                }
            }

            SglLogger.Information("[AuditLog] Integrity verified: {Count} entries, chain intact", allEntries.Count);
            return (true, -1);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns the total count of entries currently held in memory.
    /// </summary>
    public int EntryCount
    {
        get
        {
            _lock.Wait();
            try { return _recentEntries.Count; }
            finally { _lock.Release(); }
        }
    }

    private void LoadExistingChain()
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return;

            var lines = File.ReadAllLines(_logFilePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line, _jsonOptions);
                    if (entry != null)
                    {
                        _recentEntries.Add(entry);
                        _lastHash = entry.Hash ?? _lastHash;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                    SglLogger.Warning("[AuditLog] Skipping malformed JSONL line during load");
                }
            }

            // Trim to max
            if (_recentEntries.Count > MaxRecentEntries)
            {
                var excess = _recentEntries.Count - MaxRecentEntries;
                _recentEntries.RemoveRange(0, excess);
            }

            SglLogger.Information("[AuditLog] Loaded {Count} existing audit entries, last hash: {Hash}",
                _recentEntries.Count, _lastHash[..Math.Min(16, _lastHash.Length)] + "...");
        }
        catch (Exception ex)
        {
            SglLogger.Error("[AuditLog] Failed to load existing audit chain", ex);
        }
    }

    private List<AuditEntry> LoadAllEntries()
    {
        var entries = new List<AuditEntry>();

        if (!File.Exists(_logFilePath))
            return entries;

        try
        {
            var lines = File.ReadAllLines(_logFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line, _jsonOptions);
                    if (entry != null) entries.Add(entry);
                }
                catch (JsonException) { /* skip */ }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[AuditLog] Failed to load all entries for verification", ex);
        }

        return entries;
    }

    private static string ComputeEntryHash(AuditEntry entry)
    {
        // Hash is computed over all fields except the hash itself
        var payload = $"{entry.Timestamp}|{entry.EventType}|{entry.User}|{entry.Details}|{entry.IpAddress}|{entry.PreviousHash}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string TruncateForLog(string value, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}

/// <summary>
/// Individual audit log entry with hash chain link.
/// </summary>
public sealed class AuditEntry
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;

    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("previousHash")]
    public string PreviousHash { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;
}
