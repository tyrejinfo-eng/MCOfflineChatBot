namespace MCOfflineChat.Core.Events;

using MCOfflineChat.Core.Models;

public class SecurityAlertEvent : EventArgs
{
    public SecurityAlert Alert { get; init; } = null!;
}
