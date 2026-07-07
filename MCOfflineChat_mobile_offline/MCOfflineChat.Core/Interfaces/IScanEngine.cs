namespace MCOfflineChat.Core.Interfaces;

using MCOfflineChat.Core.Enums;
using MCOfflineChat.Core.Events;
using MCOfflineChat.Core.Models;

public interface IScanEngine
{
    Task<ScanSession> ScanDirectoryAsync(string path, ScanType type, IProgress<ScanProgressEvent>? progress = null, CancellationToken ct = default);
    Task<ScanResult> ScanFileAsync(string filePath, CancellationToken ct = default);
    Task<ScanSession> QuickScanAsync(IProgress<ScanProgressEvent>? progress = null, CancellationToken ct = default);
    Task<ScanSession> FullScanAsync(IProgress<ScanProgressEvent>? progress = null, CancellationToken ct = default);
    Task<ScanSession> ExtendedScanAsync(IEnumerable<string>? additionalPaths = null, IProgress<ScanProgressEvent>? progress = null, CancellationToken ct = default);
    Task<ScanSession> ScanRemovableDriveAsync(string driveLetter, IProgress<ScanProgressEvent>? progress = null, CancellationToken ct = default);
    void StartRealTimeProtection();
    void StopRealTimeProtection();
    bool IsRealTimeProtectionActive { get; }
    event EventHandler<ThreatDetectedEvent>? ThreatDetected;
}
