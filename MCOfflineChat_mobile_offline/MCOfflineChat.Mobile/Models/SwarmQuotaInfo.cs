namespace MCOfflineChat.Mobile.Models;

/// <summary>
/// Quota and usage information returned by GET /api/v1/swarm-storage/quota.
/// </summary>
public class SwarmQuotaInfo
{
    public long UsedBytes { get; set; }
    public long LimitBytes { get; set; }
    public long DailyUsedBytes { get; set; }
    public long DailyLimitBytes { get; set; }

    /// <summary>Human-readable "used / limit" storage summary (e.g. "12.3 MB / 500 MB").</summary>
    public string UsedDisplay => $"{UsedBytes / 1024.0 / 1024.0:F1} MB / {LimitBytes / 1024.0 / 1024.0:F0} MB";

    /// <summary>Overall usage as a percentage 0–100.</summary>
    public double UsagePercent => LimitBytes > 0 ? (double)UsedBytes / LimitBytes * 100.0 : 0;
}
