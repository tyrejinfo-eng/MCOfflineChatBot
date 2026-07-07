namespace MCOfflineChat.Mobile.Models;

public class SecurityAlertItem
{
    public string AlertId { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low";
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public double FinalScore { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ResponseAction { get; set; } = "None";
    public bool IsResolved { get; set; }

    public string ScoreDisplay => $"{FinalScore:P0}";
    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("g");

    public string SeverityColor => Severity switch
    {
        "Critical" => "#FF1744",
        "High" => "#FF6D00",
        "Medium" => "#FFC107",
        "Low" => "#00E676",
        _ => "#B0B0B0"
    };
}
