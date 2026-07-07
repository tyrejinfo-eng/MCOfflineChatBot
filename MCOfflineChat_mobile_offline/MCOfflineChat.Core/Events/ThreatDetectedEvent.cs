namespace MCOfflineChat.Core.Events;

using MCOfflineChat.Core.Models;

public class ThreatDetectedEvent : EventArgs
{
    public ScanResult Result { get; init; } = null!;
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}
