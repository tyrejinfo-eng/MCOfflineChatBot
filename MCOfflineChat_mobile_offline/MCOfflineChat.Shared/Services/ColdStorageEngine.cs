#pragma warning disable CA1416 // Platform compatibility warnings suppressed; this suite targets Windows desktop only.

// MCOfflineChat.Shared - Cold Storage Engine
// AES-256-GCM encrypted per-tenant blob storage with retention policies
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Metadata sidecar for a cold-storage blob.
/// </summary>
public sealed class ColdStorageBlobMeta
{
    public long OriginalSize { get; set; }
    public string Hash { get; set; } = "";
    public DateTime EncryptedAt { get; set; }
    public string Category { get; set; } = "";
    public string Metadata { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string FileName { get; set; } = "";
}

/// <summary>
/// Storage statistics for a single tenant.
/// </summary>
public sealed class ColdStorageStats
{
    public string TenantId { get; set; } = "";
    public long TotalSizeBytes { get; set; }
    public int BlobCount { get; set; }
    public List<string> Categories { get; set; } = [];
}

/// <summary>
/// Retrieved cold-storage blob with decrypted data and metadata.
/// </summary>
public sealed class ColdStorageBlob
{
    public byte[] Data { get; set; } = [];
    public ColdStorageBlobMeta Meta { get; set; } = new();
}

/// <summary>
/// AES-256-GCM encrypted per-tenant Cold Storage Engine.
/// Stores encrypted blobs with JSON metadata sidecars. Per-tenant keys are
/// derived from a master key via HKDF. Background cleanup removes blobs
/// older than the configurable retention period (default 90 days).
///
/// Storage layout: data/cold_storage/{tenantId}/{category}/{timestamp}.enc
/// Sidecar:        data/cold_storage/{tenantId}/{category}/{timestamp}.meta
/// </summary>
public sealed class ColdStorageEngine : IEngine, IDisposable
{
    // ---------------------------------------------------------------
    //  Configuration
    // ---------------------------------------------------------------

    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    /// <summary>AES-256-GCM nonce size in bytes.</summary>
    private const int NonceSize = 12;

    /// <summary>AES-256-GCM tag size in bytes.</summary>
    private const int TagSize = 16;

    // ---------------------------------------------------------------
    //  State
    // ---------------------------------------------------------------

    private readonly string _baseDirectory;
    private readonly byte[] _masterKey;
    private readonly TimeSpan _retention;
    private readonly EventBus? _eventBus;
    private readonly ConcurrentDictionary<string, byte[]> _tenantKeyCache = new();
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;
    private DateTime? _startedAt;
    private long _totalStored;
    private long _totalRetrieved;
    private long _storageBytes;
    private long _errors;
    private string? _lastError;

    public string Name => "ColdStorage";
    public bool IsRunning { get; private set; }

    /// <param name="dataDirectory">Base data directory (cold_storage will be created beneath it).</param>
    /// <param name="masterKey">32-byte master key for HKDF derivation. If null, a random key is generated.</param>
    /// <param name="retentionDays">Number of days to retain blobs before cleanup.</param>
    /// <param name="eventBus">Optional EventBus for telemetry.</param>
    public ColdStorageEngine(string dataDirectory = "data", byte[]? masterKey = null,
        int retentionDays = 90, EventBus? eventBus = null)
    {
        _baseDirectory = Path.Combine(dataDirectory, "cold_storage");
        _masterKey = masterKey ?? GenerateRandomKey();
        _retention = TimeSpan.FromDays(retentionDays);
        _eventBus = eventBus;

        Directory.CreateDirectory(_baseDirectory);
    }

    // ---------------------------------------------------------------
    //  IEngine Lifecycle
    // ---------------------------------------------------------------

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        _startedAt = DateTime.UtcNow;

        _cleanupTask = Task.Run(() => CleanupLoopAsync(_cts.Token), _cts.Token);

        // Calculate current storage size
        RecalculateStorageBytes();

        SglLogger.Information("[ColdStorage] Engine started. BaseDir={Dir}, Retention={Days}d",
            _baseDirectory, _retention.TotalDays);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return Task.CompletedTask;

        _cts?.Cancel();
        IsRunning = false;

        SglLogger.Information("[ColdStorage] Engine stopped. Stored={Stored}, Retrieved={Retrieved}, Size={Size}",
            Interlocked.Read(ref _totalStored), Interlocked.Read(ref _totalRetrieved),
            FormatBytes(Interlocked.Read(ref _storageBytes)));
        return Task.CompletedTask;
    }

    public EngineStatus GetStatus() => new()
    {
        EngineName = Name,
        IsRunning = IsRunning,
        StartedAt = _startedAt,
        EventsProcessed = Interlocked.Read(ref _totalStored),
        Errors = Interlocked.Read(ref _errors),
        LastError = _lastError,
        Metrics = new Dictionary<string, object>
        {
            ["totalStored"] = Interlocked.Read(ref _totalStored),
            ["totalRetrieved"] = Interlocked.Read(ref _totalRetrieved),
            ["storageBytes"] = Interlocked.Read(ref _storageBytes),
            ["storageFriendly"] = FormatBytes(Interlocked.Read(ref _storageBytes)),
            ["tenantKeys"] = _tenantKeyCache.Count
        }
    };

    // ---------------------------------------------------------------
    //  Store
    // ---------------------------------------------------------------

    /// <summary>
    /// Encrypts and stores a blob for the given tenant and category.
    /// Returns the blob file name (without path) for reference.
    /// </summary>
    public async Task<string> StoreAsync(string tenantId, string category, byte[] data, string metadata)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId is required");
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("category is required");
        if (data == null || data.Length == 0) throw new ArgumentException("data is required");

        var sanitizedTenant = SanitizePath(tenantId);
        var sanitizedCategory = SanitizePath(category);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"{timestamp}.enc";
        var metaFileName = $"{timestamp}.meta";

        var dir = Path.Combine(_baseDirectory, sanitizedTenant, sanitizedCategory);
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, fileName);
        var metaPath = Path.Combine(dir, metaFileName);

        try
        {
            // Derive per-tenant key via HKDF
            var key = DeriveTenantKey(tenantId);

            // Encrypt with AES-256-GCM
            var encrypted = EncryptAesGcm(data, key);
            await File.WriteAllBytesAsync(filePath, encrypted);

            // Compute hash of original data
            var hash = Convert.ToHexString(SHA256.HashData(data));

            // Write metadata sidecar
            var meta = new ColdStorageBlobMeta
            {
                OriginalSize = data.Length,
                Hash = hash,
                EncryptedAt = DateTime.UtcNow,
                Category = category,
                Metadata = metadata,
                TenantId = tenantId,
                FileName = fileName
            };

            var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(metaPath, metaJson);

            Interlocked.Increment(ref _totalStored);
            Interlocked.Add(ref _storageBytes, encrypted.Length);

            SglLogger.Information("[ColdStorage] Stored blob: {Tenant}/{Category}/{File} ({Size} → {EncSize})",
                tenantId, category, fileName, FormatBytes(data.Length), FormatBytes(encrypted.Length));

            return fileName;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            _lastError = ex.Message;
            SglLogger.Error("[ColdStorage] Store error for " + tenantId + "/" + category, ex);
            throw;
        }
    }

    // ---------------------------------------------------------------
    //  Retrieve
    // ---------------------------------------------------------------

    /// <summary>
    /// Retrieves and decrypts all blobs for the given tenant/category within the specified date range.
    /// </summary>
    public async Task<List<ColdStorageBlob>> RetrieveAsync(string tenantId, string category,
        DateTime from, DateTime to)
    {
        var results = new List<ColdStorageBlob>();

        var sanitizedTenant = SanitizePath(tenantId);
        var sanitizedCategory = SanitizePath(category);
        var dir = Path.Combine(_baseDirectory, sanitizedTenant, sanitizedCategory);

        if (!Directory.Exists(dir))
            return results;

        var key = DeriveTenantKey(tenantId);

        try
        {
            var metaFiles = Directory.GetFiles(dir, "*.meta");

            foreach (var metaPath in metaFiles)
            {
                try
                {
                    var metaJson = await File.ReadAllTextAsync(metaPath);
                    var meta = JsonSerializer.Deserialize<ColdStorageBlobMeta>(metaJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (meta == null) continue;

                    // Filter by date range
                    if (meta.EncryptedAt < from || meta.EncryptedAt > to) continue;

                    // Read and decrypt the .enc file
                    var encPath = Path.ChangeExtension(metaPath, ".enc");
                    if (!File.Exists(encPath)) continue;

                    var encrypted = await File.ReadAllBytesAsync(encPath);
                    var decrypted = DecryptAesGcm(encrypted, key);

                    results.Add(new ColdStorageBlob
                    {
                        Data = decrypted,
                        Meta = meta
                    });

                    Interlocked.Increment(ref _totalRetrieved);
                }
                catch (Exception ex)
                {
                    SglLogger.Error("[ColdStorage] Error reading blob " + metaPath, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            _lastError = ex.Message;
            SglLogger.Error("[ColdStorage] Retrieve error for " + tenantId + "/" + category, ex);
        }

        return results;
    }

    // ---------------------------------------------------------------
    //  Storage Stats
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns storage statistics for a specific tenant.
    /// </summary>
    public ColdStorageStats GetStorageStats(string tenantId)
    {
        var stats = new ColdStorageStats { TenantId = tenantId };
        var sanitizedTenant = SanitizePath(tenantId);
        var tenantDir = Path.Combine(_baseDirectory, sanitizedTenant);

        if (!Directory.Exists(tenantDir))
            return stats;

        try
        {
            var categories = Directory.GetDirectories(tenantDir);
            foreach (var catDir in categories)
            {
                var catName = Path.GetFileName(catDir);
                stats.Categories.Add(catName);

                var files = Directory.GetFiles(catDir, "*.enc");
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    stats.TotalSizeBytes += info.Length;
                    stats.BlobCount++;
                }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[ColdStorage] Stats error for tenant " + tenantId, ex);
        }

        return stats;
    }

    // ---------------------------------------------------------------
    //  AES-256-GCM Encryption
    // ---------------------------------------------------------------

    /// <summary>
    /// Encrypts data with AES-256-GCM. Output format: [12-byte nonce][ciphertext][16-byte tag].
    /// </summary>
    private static byte[] EncryptAesGcm(byte[] plaintext, byte[] key)
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // [nonce][ciphertext][tag]
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return result;
    }

    /// <summary>
    /// Decrypts AES-256-GCM data. Input format: [12-byte nonce][ciphertext][16-byte tag].
    /// </summary>
    private static byte[] DecryptAesGcm(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted data too short");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = encrypted.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encrypted, NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(encrypted, NonceSize + ciphertextLength, tag, 0, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    // ---------------------------------------------------------------
    //  HKDF Key Derivation
    // ---------------------------------------------------------------

    /// <summary>
    /// Derives a 256-bit per-tenant key from the master key via HKDF-SHA256.
    /// Results are cached for performance.
    /// </summary>
    private byte[] DeriveTenantKey(string tenantId)
    {
        return _tenantKeyCache.GetOrAdd(tenantId, id =>
        {
            var info = Encoding.UTF8.GetBytes($"ColdStorage-Tenant-{id}");
            var salt = Encoding.UTF8.GetBytes("MCOfflineChat.ColdStorage.v1");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, 32, salt, info);
        });
    }

    // ---------------------------------------------------------------
    //  Background Cleanup
    // ---------------------------------------------------------------

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                int removed = 0;
                var cutoff = DateTime.UtcNow - _retention;

                if (!Directory.Exists(_baseDirectory)) continue;

                foreach (var tenantDir in Directory.GetDirectories(_baseDirectory))
                {
                    foreach (var catDir in Directory.GetDirectories(tenantDir))
                    {
                        var metaFiles = Directory.GetFiles(catDir, "*.meta");
                        foreach (var metaPath in metaFiles)
                        {
                            try
                            {
                                var metaJson = await File.ReadAllTextAsync(metaPath, ct);
                                var meta = JsonSerializer.Deserialize<ColdStorageBlobMeta>(metaJson, new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                                if (meta != null && meta.EncryptedAt < cutoff)
                                {
                                    var encPath = Path.ChangeExtension(metaPath, ".enc");

                                    if (File.Exists(encPath))
                                    {
                                        var encSize = new FileInfo(encPath).Length;
                                        File.Delete(encPath);
                                        Interlocked.Add(ref _storageBytes, -encSize);
                                    }

                                    File.Delete(metaPath);
                                    removed++;
                                }
                            }
                            catch (Exception ex)
                            {
                                SglLogger.Error("[ColdStorage] Cleanup error for " + metaPath, ex);
                            }
                        }

                        // Remove empty category directories
                        if (Directory.Exists(catDir) && Directory.GetFiles(catDir).Length == 0 &&
                            Directory.GetDirectories(catDir).Length == 0)
                        {
                            try { Directory.Delete(catDir); } catch { /* ignore */ }
                        }
                    }
                }

                if (removed > 0)
                    SglLogger.Information("[ColdStorage] Cleanup: removed {Count} expired blobs (retention={Days}d)",
                        removed, _retention.TotalDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _errors);
                _lastError = ex.Message;
                SglLogger.Error("[ColdStorage] Cleanup loop error", ex);
            }
        }
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    private void RecalculateStorageBytes()
    {
        try
        {
            if (!Directory.Exists(_baseDirectory)) return;

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(_baseDirectory, "*.enc", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }

            Interlocked.Exchange(ref _storageBytes, total);
        }
        catch (Exception ex)
        {
            SglLogger.Error("[ColdStorage] Error calculating storage size", ex);
        }
    }

    private static string SanitizePath(string input)
    {
        // Remove path traversal and invalid chars
        var sanitized = input
            .Replace("..", "")
            .Replace("/", "")
            .Replace("\\", "")
            .Replace(":", "")
            .Replace("*", "")
            .Replace("?", "")
            .Replace("\"", "")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "");

        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private static byte[] GenerateRandomKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F1} {sizes[order]}";
    }

    // ---------------------------------------------------------------
    //  IDisposable
    // ---------------------------------------------------------------

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _tenantKeyCache.Clear();
    }
}
