namespace MCOfflineChat.Core.Interfaces;

using MCOfflineChat.Core.Enums;
using MCOfflineChat.Core.Models;

public interface INotificationService
{
    void ShowNotification(string title, string message, ThreatSeverity severity);
    void ShowThreatAlert(ThreatInfo threat);
    void ShowScanComplete(ScanSession session);
}
