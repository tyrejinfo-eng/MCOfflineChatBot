using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;

namespace MCOfflineChat.Mobile.ViewModels;

/// <summary>
/// ViewModel for the Swarm Intelligence Storage tab.
/// Connects to the server's swarm API endpoints to display and upload
/// threat intelligence signals shared across the node network.
/// </summary>
public partial class SwarmStorageViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;
    private const int MaxSignals = 50;

    // ── Status ──────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _swarmStatus = "Unknown";
    [ObservableProperty] private int _nodeCount;
    [ObservableProperty] private int _signalsReceived;
    [ObservableProperty] private int _signalsShared;
    [ObservableProperty] private string _lastSync = "Never";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ── Upload section ───────────────────────────────────────────────────────

    /// <summary>User-entered payload text for manual signal upload.</summary>
    [ObservableProperty] private string _uploadPayload = string.Empty;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private string _uploadStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasUploadStatus;

    // ── Signal feed ──────────────────────────────────────────────────────────

    public ObservableCollection<SwarmSignalItem> RecentSignals { get; } = new();

    // ── Stored files ─────────────────────────────────────────────────────────

    public ObservableCollection<SwarmStorageFileItem> StoredFiles { get; } = new();
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasDownloadStatus;

    // ── Storage quota ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _quotaDisplay = "Loading...";
    [ObservableProperty] private double _quotaPercent = 0;
    [ObservableProperty] private string _quotaDetail = "";

    public SwarmStorageViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch swarm status from GET /api/v1/swarm/status and refresh signal feed.
    /// </summary>
    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var statusEl = await GetSwarmStatusAsync();
            var signalEls = await GetRecentSignalsAsync();
            var fileEls = await GetStoredFilesAsync();
            await RefreshQuotaAsync();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (statusEl.HasValue)
                {
                    var el = statusEl.Value;
                    IsConnected = true;
                    SwarmStatus = el.TryGetProperty("status", out var st)
                        ? st.GetString() ?? "Active" : "Active";
                    NodeCount = el.TryGetProperty("connectedNodes", out var nc)
                        ? nc.GetInt32() : 0;
                    SignalsReceived = el.TryGetProperty("signalsReceived", out var sr)
                        ? sr.GetInt32() : 0;
                    SignalsShared = el.TryGetProperty("signalsShared", out var ss)
                        ? ss.GetInt32() : 0;
                    LastSync = el.TryGetProperty("lastSyncTime", out var ls)
                        ? FormatLastSync(ls.GetString()) : "Just now";
                }
                else
                {
                    IsConnected = false;
                    SwarmStatus = "Offline";
                }

                // Rebuild signal list
                RecentSignals.Clear();
                foreach (var item in signalEls.Take(MaxSignals))
                    RecentSignals.Add(item);

                // Rebuild stored files list
                StoredFiles.Clear();
                foreach (var item in fileEls)
                    StoredFiles.Add(item);
                FileCount = StoredFiles.Count;
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = false;
                SwarmStatus = "Error";
                HasError = true;
                ErrorMessage = $"Failed to refresh: {ex.Message}";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Post a threat intel signal to POST /api/v1/swarm/signal.
    /// Uses UploadPayload as the signal pattern text.
    /// </summary>
    [RelayCommand]
    private async Task UploadSignalAsync()
    {
        if (string.IsNullOrWhiteSpace(UploadPayload))
        {
            ShowUploadStatus("Enter a pattern or JSON payload before uploading.", isError: true);
            return;
        }

        IsUploading = true;
        HasUploadStatus = false;

        try
        {
            // Build a minimal ThreatIntelSignal; the server deserialises what it needs.
            object signal;
            if (UploadPayload.TrimStart().StartsWith('{'))
            {
                // User provided raw JSON — pass it through
                signal = JsonSerializer.Deserialize<JsonElement>(UploadPayload);
            }
            else
            {
                signal = new
                {
                    pattern = UploadPayload.Trim(),
                    source = "MobileClient",
                    score = 0.75,
                    timestamp = DateTimeOffset.UtcNow
                };
            }

            var response = await _apiClient.PostSwarmSignalAsync(signal);

            if (response)
            {
                ShowUploadStatus("Signal uploaded successfully.", isError: false);
                UploadPayload = string.Empty;
                // Refresh so the new signal appears in the feed
                await RefreshStatusAsync();
            }
            else
            {
                ShowUploadStatus("Server rejected the signal. Check your JWT and payload.", isError: true);
            }
        }
        catch (JsonException)
        {
            ShowUploadStatus("Invalid JSON in payload. Fix it or enter plain text.", isError: true);
        }
        catch (Exception ex)
        {
            ShowUploadStatus($"Upload error: {ex.Message}", isError: true);
        }
        finally
        {
            IsUploading = false;
        }
    }

    /// <summary>
    /// Trigger a full swarm sync via POST /api/v1/swarm/sync.
    /// </summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        HasError = false;

        try
        {
            var ok = await _apiClient.TriggerSwarmSyncAsync();
            if (ok)
            {
                LastSync = FormatLastSync(DateTimeOffset.UtcNow.ToString("o"));
                await RefreshStatusAsync();
            }
            else
            {
                HasError = true;
                ErrorMessage = "Sync request was not accepted.";
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Download a swarm storage file via GET /api/v1/swarm/storage/files/{id}/download
    /// and save it to the device's Downloads folder.
    /// </summary>
    [RelayCommand]
    private async Task DownloadFileAsync(SwarmStorageFileItem? file)
    {
        if (file is null) return;
        if (IsDownloading) return;

        IsDownloading = true;
        HasDownloadStatus = false;

        try
        {
            var (stream, serverName) = await _apiClient.DownloadSwarmFileAsync(file.FileId, file.FileName);

            if (stream is null)
            {
                ShowDownloadStatus($"Download failed for {file.ShortName}.", isError: true);
                return;
            }

            // Write to the device cache directory so the user can share it.
            var destFolder = FileSystem.CacheDirectory;
            var safeName = string.IsNullOrWhiteSpace(serverName) ? file.FileName : serverName;
            // Strip characters that are illegal in file names on any platform.
            foreach (var ch in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(ch, '_');

            var destPath = Path.Combine(destFolder, safeName);
            await using (var fileStream = File.Create(destPath))
                await stream.CopyToAsync(fileStream);

            await stream.DisposeAsync();

            ShowDownloadStatus($"Saved to cache: {safeName}", isError: false);

            // Open the file for the user to share / view.
            await Launcher.OpenAsync(new OpenFileRequest
            {
                Title = safeName,
                File = new ReadOnlyFile(destPath)
            });
        }
        catch (Exception ex)
        {
            ShowDownloadStatus($"Download error: {ex.Message}", isError: true);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Pick a file from device storage and upload it to swarm via POST /api/v1/swarm-storage/upload.
    /// </summary>
    [RelayCommand]
    private async Task UploadFileAsync()
    {
        try
        {
            IsUploading = true;
            ShowUploadStatus("Selecting file...", isError: false);

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select file to upload",
            });

            if (result == null)
            {
                IsUploading = false;
                ShowUploadStatus("Upload cancelled.", isError: false);
                return;
            }

            using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            ShowUploadStatus($"Uploading {result.FileName}...", isError: false);
            var success = await _apiClient.UploadSwarmFileAsync(result.FileName, bytes, false);
            ShowUploadStatus(
                success ? $"Uploaded {result.FileName} successfully!" : "Upload failed. Check connection.",
                isError: !success);

            if (success) await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowUploadStatus($"Upload error: {ex.Message}", isError: true);
        }
        finally
        {
            IsUploading = false;
        }
    }

    /// <summary>
    /// Delete a swarm storage file by ID via DELETE /api/v1/swarm-storage/files/{fileId}.
    /// </summary>
    [RelayCommand]
    private async Task DeleteFileAsync(string fileId)
    {
        if (string.IsNullOrEmpty(fileId)) return;
        try
        {
            ShowDownloadStatus("Deleting file...", isError: false);
            var success = await _apiClient.DeleteSwarmFileAsync(fileId);
            ShowDownloadStatus(success ? "File deleted." : "Delete failed.", isError: !success);
            if (success) await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowDownloadStatus($"Delete error: {ex.Message}", isError: true);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void OnAppearing()
    {
        _ = RefreshStatusAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<JsonElement?> GetSwarmStatusAsync()
    {
        try
        {
            var response = await _apiClient.GetSwarmStatusAsync();
            return response;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<SwarmSignalItem>> GetRecentSignalsAsync()
    {
        try
        {
            var elements = await _apiClient.GetSwarmSignalsAsync();
            var items = new List<SwarmSignalItem>();

            foreach (var el in elements)
            {
                try
                {
                    items.Add(new SwarmSignalItem
                    {
                        SignalId = el.TryGetProperty("signalId", out var id) ? id.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                        Pattern = el.TryGetProperty("pattern", out var pat) ? pat.GetString() ?? "Unknown" : "Unknown",
                        Score = el.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0.0,
                        Source = el.TryGetProperty("source", out var src) ? src.GetString() ?? "Unknown" : "Unknown",
                        Timestamp = el.TryGetProperty("timestamp", out var ts)
                            ? FormatTimestamp(ts.GetString())
                            : "Unknown",
                        Direction = el.TryGetProperty("direction", out var dir)
                            ? dir.GetString() ?? "Received"
                            : "Received"
                    });
                }
                catch { /* skip malformed elements */ }
            }

            return items;
        }
        catch
        {
            return new List<SwarmSignalItem>();
        }
    }

    private void ShowUploadStatus(string message, bool isError)
    {
        UploadStatusMessage = message;
        HasUploadStatus = true;
    }

    private void ShowDownloadStatus(string message, bool isError)
    {
        DownloadStatusMessage = message;
        HasDownloadStatus = true;
    }

    private async Task RefreshQuotaAsync()
    {
        try
        {
            var quota = await _apiClient.GetSwarmStorageQuotaAsync();
            if (quota != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    QuotaDisplay = quota.UsedDisplay;
                    QuotaPercent = quota.UsagePercent;
                    QuotaDetail = $"Daily: {quota.DailyUsedBytes / 1024.0 / 1024.0:F1} MB / {quota.DailyLimitBytes / 1024.0 / 1024.0:F0} MB";
                });
            }
        }
        catch { }
    }

    private async Task<List<SwarmStorageFileItem>> GetStoredFilesAsync()
    {
        try
        {
            var elements = await _apiClient.GetSwarmStorageFilesAsync();
            var items = new List<SwarmStorageFileItem>();

            foreach (var el in elements)
            {
                try
                {
                    items.Add(new SwarmStorageFileItem
                    {
                        FileId = el.TryGetProperty("fileId", out var id) ? id.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                        FileName = el.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "unnamed" : "unnamed",
                        SizeBytes = el.TryGetProperty("sizeBytes", out var sb) ? sb.GetInt64() : 0L,
                        Uploader = el.TryGetProperty("uploader", out var up) ? up.GetString() ?? "Unknown" : "Unknown",
                        UploadedAt = el.TryGetProperty("uploadedAt", out var ua)
                            ? FormatTimestamp(ua.GetString()) : "Unknown",
                        ContentType = el.TryGetProperty("contentType", out var ct) ? ct.GetString() ?? string.Empty : string.Empty
                    });
                }
                catch { /* skip malformed elements */ }
            }

            return items;
        }
        catch
        {
            return new List<SwarmStorageFileItem>();
        }
    }

    private static string FormatLastSync(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "Never";
        if (DateTimeOffset.TryParse(isoDate, out var dto))
        {
            var diff = DateTimeOffset.UtcNow - dto;
            if (diff.TotalSeconds < 60) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return dto.LocalDateTime.ToString("MMM d, HH:mm");
        }
        return isoDate;
    }

    private static string FormatTimestamp(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "Unknown";
        if (DateTimeOffset.TryParse(isoDate, out var dto))
            return dto.LocalDateTime.ToString("HH:mm:ss");
        return isoDate;
    }
}
