namespace MCOfflineChat.Mobile.Models;

public class EngineStatusItem
{
    public string Name { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public string Type { get; set; } = string.Empty;
    public long EventsProcessed { get; set; }
    public string Uptime { get; set; } = "N/A";

    public string StatusText => IsRunning ? "Running" : "Stopped";
    public string StatusColor => IsRunning ? "#00E676" : "#FF1744";
    public string EventsDisplay => EventsProcessed > 0 ? $"{EventsProcessed:N0} events" : "—";
}
