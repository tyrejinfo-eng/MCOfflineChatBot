using System.Text.Json;
using MCOfflineChat.Core.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Detects the system role (MainServer / CompanyServer / Client) at startup
/// by reading config files from the data directory. This determines which
/// services, engines, and API surfaces are activated.
/// </summary>
public sealed class StartupRouter
{
    /// <summary>The detected system role.</summary>
    public SystemRole Role { get; private set; } = SystemRole.Client;

    /// <summary>True if this node is the central MCOfflineChat main server.</summary>
    public bool IsMainServer => Role == SystemRole.MainServer;

    /// <summary>True if this node is a company-level server.</summary>
    public bool IsCompanyServer => Role == SystemRole.CompanyServer;

    /// <summary>True if this node is an end-user client.</summary>
    public bool IsClient => Role == SystemRole.Client;

    /// <summary>
    /// Detect the system role by inspecting config files in <paramref name="dataPath"/>.
    /// <para>Priority order:</para>
    /// <list type="number">
    ///   <item><c>system_role.json</c> — explicit <c>{"role": "MainServer"|"CompanyServer"|"Client"}</c></item>
    ///   <item><c>server_settings.json</c> exists — treated as a server (defaults to CompanyServer)</item>
    ///   <item>Fallback — Client</item>
    /// </list>
    /// </summary>
    public void DetectRole(string dataPath)
    {
        // 1. Check explicit system_role.json
        var roleFile = Path.Combine(dataPath, "system_role.json");
        if (File.Exists(roleFile))
        {
            try
            {
                var json = File.ReadAllText(roleFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("role", out var roleProp))
                {
                    var roleStr = roleProp.GetString();
                    if (Enum.TryParse<SystemRole>(roleStr, ignoreCase: true, out var parsed))
                    {
                        Role = parsed;
                        SglLogger.Information("[StartupRouter] Role from system_role.json: {Role}", Role);
                        return;
                    }

                    SglLogger.Warning("[StartupRouter] Unknown role value in system_role.json: {Value}", roleStr ?? "(null)");
                }
            }
            catch (Exception ex)
            {
                SglLogger.Error("[StartupRouter] Failed to parse system_role.json", ex);
            }
        }

        // 2. Check if server_settings.json exists (server mode)
        var serverSettings = Path.Combine(dataPath, "server_settings.json");
        if (File.Exists(serverSettings))
        {
            // Parse server_settings.json to detect if this is the Main Server
            // The Main Server has "systemRole": "MainServer" or can be identified
            // by its publicDomain matching the known main server domain.
            try
            {
                var ssJson = File.ReadAllText(serverSettings);
                using var ssDoc = JsonDocument.Parse(ssJson);
                var root = ssDoc.RootElement;

                // Check explicit systemRole field first
                if (root.TryGetProperty("systemRole", out var srProp))
                {
                    var srStr = srProp.GetString();
                    if (Enum.TryParse<SystemRole>(srStr, ignoreCase: true, out var srParsed))
                    {
                        Role = srParsed;
                        SglLogger.Information("[StartupRouter] Role from server_settings.json systemRole: {Role}", Role);
                        return;
                    }
                }

                // Auto-detect MainServer: if publicDomain matches our known main server domain
                if (root.TryGetProperty("server", out var serverObj))
                {
                    if (serverObj.TryGetProperty("publicDomain", out var domainProp))
                    {
                        var domain = domainProp.GetString();
                        if (string.Equals(domain, "syntheticgamelabs.dpdns.org", StringComparison.OrdinalIgnoreCase))
                        {
                            Role = SystemRole.MainServer;
                            SglLogger.Information("[StartupRouter] Main server detected via publicDomain: {Domain}", domain ?? "null");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SglLogger.Error("[StartupRouter] Failed to parse server_settings.json for role detection", ex);
            }

            Role = SystemRole.CompanyServer;
            SglLogger.Information("[StartupRouter] server_settings.json found — defaulting to CompanyServer");
            return;
        }

        // 3. Fallback to Client
        Role = SystemRole.Client;
        SglLogger.Information("[StartupRouter] No role config found — defaulting to Client");
    }
}
