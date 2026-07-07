using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MCOfflineChat.Core.Interfaces;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Scheduled backup engine with incremental support and AES-256-GCM encryption.
/// v1.1.56: Provides hourly telemetry, daily models, weekly full backups.
/// </summary>
public sealed class BackupEngine : IEngine, IDisposable
{
    public string Name => "Backup";
    public bool IsRunning { get; private set; }

    private readonly string _dataDirectory;
    private readonly string _backupDirectory;
    private readonly EventBus? _eventBus;
    private CancellationTokenSource? _cts;
    private Task? _schedulerLoop;
    private DateTime? _startedAt;
    private long _backupsCompleted;
    private long _errors;
    private string? _lastError;
    private DateTime? _lastBackupTime;
    private readonly ConcurrentQueue<BackupRecord> _history = new();
    private const int MaxHistory = 100;

    /// <summary>Tracks last-known write times for incremental backup.</summary>
    private Dictionary<string, DateTime> _lastBackupState = new();

    // Schedule intervals
    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ModelsInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FullInterval = TimeSpan.FromDays(7);

    private DateTime _nextTelemetryBackup;
    private DateTime _nextModelBackup;
    private DateTime _nextFullBackup;

    public BackupEngine(string dataDirectory, EventBus? eventBus = null)
    {
        _dataDirectory = dataDirectory;
        _backupDirectory = Path.Combine(dataDirectory, "backups");
        _eventBus = eventBus;

        Directory.CreateDirectory(_backupDirectory);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        _startedAt = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        _nextTelemetryBackup = now.Add(TelemetryInterval);
        _nextModelBackup = now.Add(ModelsInterval);
        _nextFullBackup = now.Add(FullInterval);

        _schedulerLoop = Task.Run(() => SchedulerLoopAsync(_cts.Token), _cts.Token);
        SglLogger.Information("[BackupEngine] Started — backup dir: {Dir}", _backupDirectory);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;

        try
        {
            _cts?.Cancel();
            if (_schedulerLoop != null)
                await _schedulerLoop.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _schedulerLoop = null;
        }

        SglLogger.Information("[BackupEngine] Stopped — {Count} backups completed", _backupsCompleted);
    }

    public EngineStatus GetStatus() => new()
    {
        EngineName = Name,
        IsRunning = IsRunning,
        StartedAt = _startedAt,
        EventsProcessed = Interlocked.Read(ref _backupsCompleted),
        Errors = Interlocked.Read(ref _errors),
        LastError = _lastError,
        Metrics = new Dictionary<string, object>
        {
            ["backups_completed"] = Interlocked.Read(ref _backupsCompleted),
            ["last_backup"] = _lastBackupTime?.ToString("o") ?? "never",
            ["backup_directory"] = _backupDirectory,
            ["next_telemetry"] = _nextTelemetryBackup.ToString("o"),
            ["next_model"] = _nextModelBackup.ToString("o"),
            ["next_full"] = _nextFullBackup.ToString("o")
        }
    };

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>Trigger an immediate backup of the specified type.</summary>
    public async Task<BackupRecord> TriggerBackupAsync(BackupType type, string? encryptionKey = null,
        CancellationToken ct = default)
    {
        var record = new BackupRecord
        {
            Type = type,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            var sourceDirs = GetSourceDirectories(type);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"backup_{type.ToString().ToLowerInvariant()}_{timestamp}.zip";
            var backupPath = Path.Combine(_backupDirectory, fileName);

            // Create ZIP archive
            await CreateBackupArchiveAsync(sourceDirs, backupPath, ct).ConfigureAwait(false);

            // Encrypt if key provided
            if (!string.IsNullOrWhiteSpace(encryptionKey))
            {
                var encPath = backupPath + ".enc";
                await EncryptFileAsync(backupPath, encPath, encryptionKey, ct).ConfigureAwait(false);
                File.Delete(backupPath);
                backupPath = encPath;
            }

            var info = new FileInfo(backupPath);
            record.FilePath = backupPath;
            record.SizeBytes = info.Exists ? info.Length : 0;
            record.CompletedAt = DateTime.UtcNow;
            record.Success = true;

            Interlocked.Increment(ref _backupsCompleted);
            _lastBackupTime = DateTime.UtcNow;

            SglLogger.Information("[BackupEngine] {Type} backup completed: {Path} ({Size:F1} MB)",
                type, backupPath, record.SizeBytes / (1024.0 * 1024));

            _eventBus?.Publish("backup.completed", Name, data: new Dictionary<string, object>
            {
                ["type"] = type.ToString(),
                ["path"] = backupPath,
                ["size_bytes"] = record.SizeBytes
            });
        }
        catch (Exception ex)
        {
            record.CompletedAt = DateTime.UtcNow;
            record.Success = false;
            record.Error = ex.Message;
            Interlocked.Increment(ref _errors);
            _lastError = ex.Message;

            SglLogger.Error("[BackupEngine] {Type} backup failed", ex);
        }

        AddToHistory(record);
        return record;
    }

    public List<BackupRecord> GetHistory(int count = 20) =>
        _history.Reverse().Take(count).ToList();

    public List<string> ListBackupFiles()
    {
        if (!Directory.Exists(_backupDirectory)) return [];
        return Directory.GetFiles(_backupDirectory, "backup_*.*")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>
    /// Trigger an incremental backup — only files changed since the last recorded backup.
    /// State is persisted to {backupDirectory}/last_backup_state.json.
    /// </summary>
    public async Task<BackupRecord> TriggerIncrementalBackupAsync(BackupType type,
        string? encryptionKey = null, CancellationToken ct = default)
    {
        var record = new BackupRecord
        {
            Type = type,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            var stateFilePath = Path.Combine(_backupDirectory, "last_backup_state.json");
            await LoadBackupStateAsync(stateFilePath, ct).ConfigureAwait(false);

            var sourceDirs = GetSourceDirectories(type);
            var changedFiles = new List<string>();

            foreach (var source in sourceDirs)
            {
                if (File.Exists(source))
                {
                    var lwt = File.GetLastWriteTimeUtc(source);
                    if (!_lastBackupState.TryGetValue(source, out var prev) || lwt > prev)
                        changedFiles.Add(source);
                }
                else if (Directory.Exists(source))
                {
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var lwt = File.GetLastWriteTimeUtc(file);
                        if (!_lastBackupState.TryGetValue(file, out var prev) || lwt > prev)
                            changedFiles.Add(file);
                    }
                }
            }

            if (changedFiles.Count == 0)
            {
                record.CompletedAt = DateTime.UtcNow;
                record.Success = true;
                record.SizeBytes = 0;
                SglLogger.Information("[BackupEngine] Incremental {Type}: no changes detected, skipping", type);
                AddToHistory(record);
                return record;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"backup_{type.ToString().ToLowerInvariant()}_incr_{timestamp}.zip";
            var backupPath = Path.Combine(_backupDirectory, fileName);

            // Create ZIP with only changed files
            await CreateIncrementalArchiveAsync(changedFiles, backupPath, ct).ConfigureAwait(false);

            // Encrypt if key provided
            if (!string.IsNullOrWhiteSpace(encryptionKey))
            {
                var encPath = backupPath + ".enc";
                await EncryptFileAsync(backupPath, encPath, encryptionKey, ct).ConfigureAwait(false);
                File.Delete(backupPath);
                backupPath = encPath;
            }

            // Update state with current write times
            foreach (var file in changedFiles)
                _lastBackupState[file] = File.GetLastWriteTimeUtc(file);

            await SaveBackupStateAsync(stateFilePath, ct).ConfigureAwait(false);

            var info = new FileInfo(backupPath);
            record.FilePath = backupPath;
            record.SizeBytes = info.Exists ? info.Length : 0;
            record.CompletedAt = DateTime.UtcNow;
            record.Success = true;

            Interlocked.Increment(ref _backupsCompleted);
            _lastBackupTime = DateTime.UtcNow;

            SglLogger.Information("[BackupEngine] Incremental {Type} backup completed: {Path} ({Count} files, {Size:F1} MB)",
                type, backupPath, changedFiles.Count, record.SizeBytes / (1024.0 * 1024));

            _eventBus?.Publish("backup.incremental.completed", Name, data: new Dictionary<string, object>
            {
                ["type"] = type.ToString(),
                ["path"] = backupPath,
                ["size_bytes"] = record.SizeBytes,
                ["files_changed"] = changedFiles.Count
            });
        }
        catch (Exception ex)
        {
            record.CompletedAt = DateTime.UtcNow;
            record.Success = false;
            record.Error = ex.Message;
            Interlocked.Increment(ref _errors);
            _lastError = ex.Message;

            SglLogger.Error("[BackupEngine] Incremental {Type} backup failed", ex);
        }

        AddToHistory(record);
        return record;
    }

    /// <summary>
    /// Restore a backup archive to the data directory.
    /// If the file ends with .enc, it will be decrypted first using AES-256-GCM.
    /// </summary>
    public async Task<bool> RestoreBackupAsync(string backupFilePath, string? encryptionKey = null,
        CancellationToken ct = default)
    {
        try
        {
            SglLogger.Information("[BackupEngine] Starting restore from: {Path}", backupFilePath);

            if (!File.Exists(backupFilePath))
            {
                SglLogger.Error("[BackupEngine] Restore failed — file not found",
                    new FileNotFoundException("Backup file not found", backupFilePath));
                return false;
            }

            var zipPath = backupFilePath;

            // Decrypt if encrypted
            if (backupFilePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(encryptionKey))
                {
                    SglLogger.Error("[BackupEngine] Restore failed — encrypted file requires encryption key",
                        new InvalidOperationException("Encryption key required for .enc files"));
                    return false;
                }

                zipPath = Path.Combine(_backupDirectory,
                    $"restore_temp_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
                SglLogger.Information("[BackupEngine] Decrypting backup to temporary file: {Path}", zipPath);
                await DecryptFileAsync(backupFilePath, zipPath, encryptionKey, ct).ConfigureAwait(false);
            }

            SglLogger.Information("[BackupEngine] Extracting archive to: {Dir}", _dataDirectory);
            ZipFile.ExtractToDirectory(zipPath, _dataDirectory, overwriteFiles: true);

            // Clean up temp decrypted file
            if (zipPath != backupFilePath && File.Exists(zipPath))
                File.Delete(zipPath);

            SglLogger.Information("[BackupEngine] Restore completed successfully from: {Path}", backupFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            _lastError = ex.Message;
            SglLogger.Error("[BackupEngine] Restore failed", ex);
            return false;
        }
    }

    /// <summary>Compute SHA-256 hash of a file, returned as a hex string.</summary>
    public static string ComputeSHA256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>Validate that a backup archive can be opened as a valid ZIP file.</summary>
    public static bool ValidateBackup(string backupPath)
    {
        try
        {
            using var stream = File.OpenRead(backupPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            // Iterate entries to ensure the archive is not corrupt
            foreach (var entry in archive.Entries)
            {
                _ = entry.FullName;
            }
            return true;
        }
        catch (Exception ex)
        {
            SglLogger.Error("[BackupEngine] Backup validation failed for: " + backupPath, ex);
            return false;
        }
    }

    /// <summary>
    /// Returns a summary string with total backup count, total size, and last backup time.
    /// </summary>
    public string GetBackupSummary()
    {
        var files = ListBackupFiles();
        long totalSize = 0;
        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            if (fi.Exists) totalSize += fi.Length;
        }

        var lastBackup = _lastBackupTime.HasValue
            ? _lastBackupTime.Value.ToString("o")
            : "never";

        return $"Backups: {files.Count} | Total size: {totalSize / (1024.0 * 1024):F2} MB | Last backup: {lastBackup}";
    }

    // ── Scheduler ───────────────────────────────────────────────────────

    private async Task SchedulerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);

                var now = DateTime.UtcNow;

                if (now >= _nextTelemetryBackup)
                {
                    await TriggerIncrementalBackupAsync(BackupType.Telemetry, ct: ct).ConfigureAwait(false);
                    _nextTelemetryBackup = now.Add(TelemetryInterval);
                }

                if (now >= _nextModelBackup)
                {
                    await TriggerBackupAsync(BackupType.Models, ct: ct).ConfigureAwait(false);
                    _nextModelBackup = now.Add(ModelsInterval);
                }

                if (now >= _nextFullBackup)
                {
                    await TriggerBackupAsync(BackupType.Full, ct: ct).ConfigureAwait(false);
                    _nextFullBackup = now.Add(FullInterval);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errors);
                _lastError = ex.Message;
                SglLogger.Error("[BackupEngine] Scheduler error", ex);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private List<string> GetSourceDirectories(BackupType type) => type switch
    {
        BackupType.Telemetry =>
        [
            Path.Combine(_dataDirectory, "telemetry"),
            Path.Combine(_dataDirectory, "hot"),
            Path.Combine(_dataDirectory, "warm")
        ],
        BackupType.Models =>
        [
            Path.Combine(_dataDirectory, "ml_models"),
            Path.Combine(_dataDirectory, "rules"),
            Path.Combine(_dataDirectory, "playbooks"),
            Path.Combine(_dataDirectory, "yara_rules")
        ],
        BackupType.Full =>
        [
            _dataDirectory
        ],
        BackupType.Config =>
        [
            Path.Combine(_dataDirectory, "rules"),
            Path.Combine(_dataDirectory, "playbooks"),
            Path.Combine(_dataDirectory, "soc_config.json")
        ],
        _ => [_dataDirectory]
    };

    private static async Task CreateBackupArchiveAsync(List<string> sources, string outputPath,
        CancellationToken ct)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var source in sources)
        {
            if (File.Exists(source))
            {
                var entry = archive.CreateEntry(Path.GetFileName(source), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(source);
                await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
            }
            else if (Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(Path.GetDirectoryName(source)!, file);
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file);
                    await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task EncryptFileAsync(string inputPath, string outputPath, string password,
        CancellationToken ct)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);

        var plaintext = await File.ReadAllBytesAsync(inputPath, ct).ConfigureAwait(false);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // File format: [salt:16][nonce:12][tag:16][ciphertext:N]
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await output.WriteAsync(salt, ct).ConfigureAwait(false);
        await output.WriteAsync(nonce, ct).ConfigureAwait(false);
        await output.WriteAsync(tag, ct).ConfigureAwait(false);
        await output.WriteAsync(ciphertext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Decrypt an AES-256-GCM encrypted file.
    /// File format: [salt:16][nonce:12][tag:16][ciphertext:N]
    /// </summary>
    private static async Task DecryptFileAsync(string inputPath, string outputPath, string password,
        CancellationToken ct)
    {
        var fileBytes = await File.ReadAllBytesAsync(inputPath, ct).ConfigureAwait(false);

        var salt = fileBytes.AsSpan(0, 16).ToArray();
        var nonce = fileBytes.AsSpan(16, 12).ToArray();
        var tag = fileBytes.AsSpan(28, 16).ToArray();
        var ciphertext = fileBytes.AsSpan(44).ToArray();

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        await File.WriteAllBytesAsync(outputPath, plaintext, ct).ConfigureAwait(false);
    }

    /// <summary>Create a ZIP archive containing only the specified changed files.</summary>
    private static async Task CreateIncrementalArchiveAsync(List<string> changedFiles, string outputPath,
        CancellationToken ct)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in changedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = Path.GetFileName(file);
            // Use full relative-like path to avoid collisions
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                var dirName = Path.GetFileName(dir);
                entryName = Path.Combine(dirName, Path.GetFileName(file));
            }

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);
            await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Load incremental backup state from JSON file.</summary>
    private async Task LoadBackupStateAsync(string stateFilePath, CancellationToken ct)
    {
        if (!File.Exists(stateFilePath))
        {
            _lastBackupState = new Dictionary<string, DateTime>();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(stateFilePath, ct).ConfigureAwait(false);
            _lastBackupState = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json)
                               ?? new Dictionary<string, DateTime>();
        }
        catch (Exception ex)
        {
            SglLogger.Error("[BackupEngine] Failed to load backup state, starting fresh", ex);
            _lastBackupState = new Dictionary<string, DateTime>();
        }
    }

    /// <summary>Persist incremental backup state to JSON file.</summary>
    private async Task SaveBackupStateAsync(string stateFilePath, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(_lastBackupState, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(stateFilePath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SglLogger.Error("[BackupEngine] Failed to save backup state", ex);
        }
    }

    private void AddToHistory(BackupRecord record)
    {
        _history.Enqueue(record);
        while (_history.Count > MaxHistory) _history.TryDequeue(out _);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public enum BackupType
{
    Telemetry,
    Models,
    Full,
    Config
}

public sealed class BackupRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public BackupType Type { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    public string? Error { get; set; }
}
