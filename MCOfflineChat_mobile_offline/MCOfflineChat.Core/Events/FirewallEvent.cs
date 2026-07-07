namespace MCOfflineChat.Core.Events;

using MCOfflineChat.Core.Enums;

public class FirewallEvent : EventArgs
{
    public string RuleName { get; init; } = string.Empty;
    public FirewallAction Action { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
