using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Manages admin-controlled shutdown policies for client endpoints.
/// The sub-server/main server sends policy JSON that determines whether
/// a client is allowed to shut down, stop its service, or disable protection.
/// </summary>
public sealed class AdminShutdownPolicyService
{
    private ClientPolicy _currentPolicy = new();
    private readonly string _policyPath;
    private DateTime _lastFetched;

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AdminShutdownPolicyService(string dataRoot)
    {
        _policyPath = Path.Combine(dataRoot, "client_policy.json");
        LoadLocalPolicy();
    }

    /// <summary>Current client protection policy.</summary>
    public ClientPolicy Policy => _currentPolicy;

    /// <summary>Whether the user is allowed to shut down the client.</summary>
    public bool AllowShutdown => _currentPolicy.AllowUserShutdown;

    /// <summary>Whether the user is allowed to stop the protection service.</summary>
    public bool AllowServiceStop => _currentPolicy.AllowServiceStop;

    /// <summary>Whether tamper protection is enabled.</summary>
    public bool TamperProtectionEnabled => _currentPolicy.TamperProtection;

    /// <summary>Whether screen monitoring is enabled.</summary>
    public bool ScreenMonitoringEnabled => _currentPolicy.ScreenMonitoringEnabled;

    /// <summary>
    /// Update policy from server response.
    /// </summary>
    public void UpdatePolicy(ClientPolicy policy)
    {
        _currentPolicy = policy ?? new ClientPolicy();
        _lastFetched = DateTime.UtcNow;
        SaveLocalPolicy();
        SglLogger.Information("[AdminPolicy] Policy updated: AllowShutdown={0}, TamperProtection={1}",
            _currentPolicy.AllowUserShutdown, _currentPolicy.TamperProtection);
    }

    /// <summary>
    /// Fetch policy from server asynchronously.
    /// </summary>
    public async Task RefreshFromServerAsync(HttpClient httpClient, string serverUrl, string deviceId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{serverUrl}/api/v1/client/policy/{deviceId}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var policy = JsonSerializer.Deserialize<ClientPolicy>(json, s_json);
                if (policy != null)
                    UpdatePolicy(policy);
            }
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[AdminPolicy] Failed to fetch policy from server: {0}", ex.Message);
        }
    }

    private void LoadLocalPolicy()
    {
        try
        {
            if (File.Exists(_policyPath))
            {
                var json = File.ReadAllText(_policyPath);
                var policy = JsonSerializer.Deserialize<ClientPolicy>(json, s_json);
                if (policy != null)
                {
                    _currentPolicy = policy;
                    SglLogger.Information("[AdminPolicy] Loaded local policy");
                }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[AdminPolicy] Failed to load local policy: {0}", ex.Message);
        }
    }

    private void SaveLocalPolicy()
    {
        try
        {
            var dir = Path.GetDirectoryName(_policyPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_currentPolicy, s_json);
            File.WriteAllText(_policyPath, json);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[AdminPolicy] Failed to save local policy: {0}", ex.Message);
        }
    }
}

/// <summary>
/// Client protection policy received from admin server.
/// </summary>
public sealed class ClientPolicy
{
    public bool AllowUserShutdown { get; set; } = true;
    public bool AllowServiceStop { get; set; } = true;
    public bool TamperProtection { get; set; }
    public bool ScreenMonitoringEnabled { get; set; }
    public bool ClientProtection { get; set; } = true;
    public int PolicyRefreshIntervalSeconds { get; set; } = 300;
    public string? ServerUrl { get; set; }
}
