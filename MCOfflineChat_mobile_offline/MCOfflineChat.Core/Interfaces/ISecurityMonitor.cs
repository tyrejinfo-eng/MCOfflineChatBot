namespace MCOfflineChat.Core.Interfaces;

using MCOfflineChat.Core.Events;
using MCOfflineChat.Core.Models;

public interface ISecurityMonitor
{
    void StartAllMonitors();
    void StopAllMonitors();
    IReadOnlyList<SecurityAlert> GetActiveAlerts();
    event EventHandler<SecurityAlertEvent>? AlertRaised;
}
