using MCOfflineChat.Core.Enums;

namespace MCOfflineChat.Core.Models;

public class ThreatInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public ThreatSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public DateTime FirstSeen { get; set; }
}
