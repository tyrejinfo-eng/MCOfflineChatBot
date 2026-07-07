namespace MCOfflineChat.Core.Interfaces;

using MCOfflineChat.Core.Models;

public interface IFirewallManager
{
    IReadOnlyList<FirewallRule> GetAllRules();
    void AddRule(FirewallRule rule);
    void RemoveRule(string name);
    void EnableRule(string name);
    void DisableRule(string name);
    void ApplyPreset(FirewallPreset preset);
    void RevertPreset(FirewallPreset preset);
    IReadOnlyList<NetworkConnection> GetActiveConnections();
}
