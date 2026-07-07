// MCOfflineChat.Shared - JWT Token Blacklist Service
// Provides runtime token revocation for banned/disabled users
// v1.1.52: Persists blacklist to data/token_blacklist.json across server restarts
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Static token blacklist service. Tracks blacklisted usernames and JWT token IDs.
/// Checked by JwtAuthMiddleware on every authenticated request.
/// Persists to data/token_blacklist.json so blacklists survive server restarts.
/// </summary>
public static class TokenBlacklistService
{
    // Username -> blacklist expiry time
    private static readonly ConcurrentDictionary<string, DateTime> _blacklistedUsers = new(StringComparer.OrdinalIgnoreCase);

    // Cleanup timer
    private static readonly Timer _cleanupTimer = new(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

    // Persistence path (set via SetDataPath)
    private static string? _persistPath;
    private static readonly object _saveLock = new();

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Configure the data directory for persistence. Call during startup.
    /// Loads any existing blacklist from data/token_blacklist.json.
    /// </summary>
    public static void SetDataPath(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _persistPath = Path.Combine(dataDir, "token_blacklist.json");
        LoadFromDisk();
    }

    /// <summary>Blacklist a user for 24 hours (matching JWT expiry).</summary>
    public static void BlacklistUser(string username, TimeSpan? duration = null)
    {
        var expiry = DateTime.UtcNow + (duration ?? TimeSpan.FromHours(24));
        _blacklistedUsers[username] = expiry;
        SaveToDisk();
    }

    /// <summary>Remove a user from the blacklist.</summary>
    public static void RemoveFromBlacklist(string username)
    {
        _blacklistedUsers.TryRemove(username, out _);
        SaveToDisk();
    }

    /// <summary>Check if a username is blacklisted.</summary>
    public static bool IsBlacklisted(string? username)
    {
        if (string.IsNullOrEmpty(username)) return false;
        if (_blacklistedUsers.TryGetValue(username, out var expiry))
        {
            if (expiry > DateTime.UtcNow) return true;
            _blacklistedUsers.TryRemove(username, out _);
        }
        return false;
    }

    /// <summary>Get all currently blacklisted users.</summary>
    public static IReadOnlyDictionary<string, DateTime> GetBlacklistedUsers()
        => _blacklistedUsers;

    private static void Cleanup()
    {
        var now = DateTime.UtcNow;
        var removed = false;
        foreach (var kvp in _blacklistedUsers)
        {
            if (kvp.Value <= now)
            {
                _blacklistedUsers.TryRemove(kvp.Key, out _);
                removed = true;
            }
        }
        if (removed) SaveToDisk();
    }

    private static void SaveToDisk()
    {
        if (_persistPath == null) return;
        try
        {
            lock (_saveLock)
            {
                var snapshot = _blacklistedUsers.ToDictionary(kv => kv.Key, kv => kv.Value);
                var json = JsonSerializer.Serialize(snapshot, _jsonOpts);
                File.WriteAllText(_persistPath, json);
            }
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[TokenBlacklist] Failed to persist: {Error}", ex.Message);
        }
    }

    private static void LoadFromDisk()
    {
        if (_persistPath == null || !File.Exists(_persistPath)) return;
        try
        {
            var json = File.ReadAllText(_persistPath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            if (entries == null) return;

            var now = DateTime.UtcNow;
            var loaded = 0;
            foreach (var (username, expiry) in entries)
            {
                if (expiry > now)
                {
                    _blacklistedUsers[username] = expiry;
                    loaded++;
                }
            }
            if (loaded > 0)
                SglLogger.Information("[TokenBlacklist] Loaded {Count} blacklist entries from disk", loaded);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[TokenBlacklist] Failed to load from disk: {Error}", ex.Message);
        }
    }
}
