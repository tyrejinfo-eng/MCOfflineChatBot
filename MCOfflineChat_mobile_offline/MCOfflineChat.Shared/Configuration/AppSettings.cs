using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCOfflineChat.Shared.Configuration;

public class AppSettings
{
    [JsonIgnore]
    private string? _filePath;

    [JsonPropertyName("general")]
    public GeneralSettings General { get; set; } = new();

    /// <summary>v1.1.77: When false, suppresses all outbound telemetry collection.</summary>
    [JsonPropertyName("telemetryEnabled")]
    public bool TelemetryEnabled { get; set; } = true;

    [JsonPropertyName("scanner")]
    public ScannerSettings Scanner { get; set; } = new();

    [JsonPropertyName("firewall")]
    public FirewallSettings Firewall { get; set; } = new();

    [JsonPropertyName("llm")]
    public LlmSettings Llm { get; set; } = new();

    [JsonPropertyName("security")]
    public SecuritySettings Security { get; set; } = new();

    [JsonPropertyName("deploymentMode")]
    public string DeploymentMode { get; set; } = "Client";

    [JsonPropertyName("server")]
    public ServerSettings Server { get; set; } = new();

    [JsonPropertyName("client")]
    public ClientSettings Client { get; set; } = new();

    [JsonIgnore]
    public bool IsServerMode => DeploymentMode.Equals("Server", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsClientMode => DeploymentMode.Equals("Client", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings LoadFromFile(string filePath)
    {
        AppSettings settings;
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                settings = new AppSettings();
            }
        }
        else
        {
            settings = new AppSettings();
        }

        settings._filePath = filePath;
        return settings;
    }

    public void SaveToFile(string? filePath = null)
    {
        var path = filePath ?? _filePath;
        if (string.IsNullOrEmpty(path)) return;

        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        // v1.1.52: Encrypt sensitive fields before writing to disk
        MCOfflineChat.Shared.Services.SecureStorage.EncryptSettings(this);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
        // Decrypt back so in-memory values remain usable
        MCOfflineChat.Shared.Services.SecureStorage.DecryptSettings(this);
    }
}

public class GeneralSettings
{
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("notificationSounds")]
    public bool NotificationSounds { get; set; } = true;
}

public class ScannerSettings
{
    [JsonPropertyName("realTimeProtection")]
    public bool RealTimeProtection { get; set; } = true;

    [JsonPropertyName("scanExclusions")]
    public List<string> ScanExclusions { get; set; } = new();

    [JsonPropertyName("scheduledScanTime")]
    public TimeSpan? ScheduledScanTime { get; set; }
}

public class FirewallSettings
{
    [JsonPropertyName("defaultAction")]
    public string DefaultAction { get; set; } = "Block";

    [JsonPropertyName("loggingVerbosity")]
    public string LoggingVerbosity { get; set; } = "Normal";
}

public class LlmSettings
{
    [JsonPropertyName("contextSize")]
    public int ContextSize { get; set; } = 8192;

    [JsonPropertyName("gpuLayers")]
    public int GpuLayers { get; set; } = 33;

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.8f;

    [JsonPropertyName("modelPath")]
    public string ModelPath { get; set; } = string.Empty;

    [JsonPropertyName("ttsServerUrl")]
    public string? TtsServerUrl { get; set; } = "http://localhost:8091";

    [JsonPropertyName("ttsVoice")]
    public string TtsVoice { get; set; } = "Chelsie";
}

public class SecuritySettings
{
    [JsonPropertyName("enableRemoteAccessDetection")]
    public bool EnableRemoteAccessDetection { get; set; } = true;

    [JsonPropertyName("enableBadUsbDetection")]
    public bool EnableBadUsbDetection { get; set; } = true;

    [JsonPropertyName("enableAiDetection")]
    public bool EnableAiDetection { get; set; } = true;

    [JsonPropertyName("enableRegistryWatcher")]
    public bool EnableRegistryWatcher { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, native DLL integrity verification will throw a
    /// <see cref="System.Security.SecurityException"/> on any hash mismatch or missing file,
    /// preventing the application from starting with potentially tampered binaries.
    /// Default is <c>false</c> (warn-only mode).
    /// </summary>
    [JsonPropertyName("nativeDllFailHard")]
    public bool NativeDllFailHard { get; set; } = false;
}

public class ServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 5000;

    [JsonPropertyName("legacyPort")]
    public int LegacyPort { get; set; } = 7743;

    [JsonPropertyName("listenAddress")]
    public string ListenAddress { get; set; } = "0.0.0.0";

    [JsonPropertyName("maxClients")]
    public int MaxClients { get; set; } = 100;

    [JsonPropertyName("publicDomain")]
    public string PublicDomain { get; set; } = "syntheticgamelabs.dpdns.org";

    [JsonPropertyName("tunnelToken")]
    public string TunnelToken { get; set; } = string.Empty;

    [JsonPropertyName("cloudflareZoneId")]
    public string CloudflareZoneId { get; set; } = string.Empty;

    [JsonPropertyName("cloudflareAccountId")]
    public string CloudflareAccountId { get; set; } = string.Empty;

    [JsonPropertyName("originCertPath")]
    public string OriginCertPath { get; set; } = string.Empty;

    [JsonPropertyName("originKeyPath")]
    public string OriginKeyPath { get; set; } = string.Empty;

    [JsonPropertyName("cloudflareApiEmail")]
    public string CloudflareApiEmail { get; set; } = string.Empty;

    [JsonPropertyName("cloudflareApiKey")]
    public string CloudflareApiKey { get; set; } = string.Empty;

    [JsonPropertyName("adminPassword")]
    public string AdminPassword { get; set; } = "";

    [JsonPropertyName("enableHttps")]
    public bool EnableHttps { get; set; } = false;

    /// <summary>v1.1.72: Controls whether Swagger UI is exposed. Default false (disabled in production unless explicitly enabled).</summary>
    [JsonPropertyName("enableSwagger")]
    public bool EnableSwagger { get; set; } = false;

    [JsonPropertyName("httpsPort")]
    public int HttpsPort { get; set; } = 5001;

    [JsonPropertyName("certificatePath")]
    public string CertificatePath { get; set; } = string.Empty;

    [JsonPropertyName("certificatePassword")]
    public string CertificatePassword { get; set; } = string.Empty;

    [JsonIgnore]
    public string PublicUrl => $"https://{PublicDomain}";

    /// <summary>
    /// When true, the admin must change the default password on first login.
    /// Set by startup validation when AdminPassword equals "changeme".
    /// </summary>
    [JsonIgnore]
    public static bool ForceAdminPasswordChange { get; set; }

    /// <summary>Configurable per-endpoint-group rate limits. Falls back to defaults if null.</summary>
    [JsonPropertyName("rateLimits")]
    public RateLimitSettings? RateLimits { get; set; }

    /// <summary>v1.1.75: MFA enforcement policy. Controls which roles require MFA enrollment.</summary>
    [JsonPropertyName("mfa")]
    public MfaSettings? Mfa { get; set; }
}

/// <summary>
/// Configurable rate limit settings for the API server.
/// Loaded from settings JSON; falls back to sensible defaults if omitted.
/// </summary>
public class RateLimitSettings
{
    [JsonPropertyName("authPerMinute")]
    public int AuthPerMinute { get; set; } = 10;

    [JsonPropertyName("scanPerMinute")]
    public int ScanPerMinute { get; set; } = 100;

    [JsonPropertyName("chatPerMinute")]
    public int ChatPerMinute { get; set; } = 30;

    [JsonPropertyName("adminPerMinute")]
    public int AdminPerMinute { get; set; } = 60;

    [JsonPropertyName("telemetryPerMinute")]
    public int TelemetryPerMinute { get; set; } = 500;

    [JsonPropertyName("defaultPerMinute")]
    public int DefaultPerMinute { get; set; } = 300;

    [JsonPropertyName("maxFailedLoginAttempts")]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    [JsonPropertyName("lockoutMinutes")]
    public int LockoutMinutes { get; set; } = 15;

    [JsonPropertyName("perUserRequestsPerMinute")]
    public int PerUserRequestsPerMinute { get; set; } = 200;
}

/// <summary>
/// MFA enforcement policy settings for the API server.
/// Controls which roles are required to enroll in multi-factor authentication
/// and provides a grace period for new accounts to complete enrollment.
/// </summary>
public class MfaSettings
{
    /// <summary>When true, users with the Admin role must enroll in MFA.</summary>
    [JsonPropertyName("requireForAdmin")]
    public bool RequireForAdmin { get; set; } = true;

    /// <summary>When true, users with the SOCAnalyst role must enroll in MFA.</summary>
    [JsonPropertyName("requireForSocAnalyst")]
    public bool RequireForSocAnalyst { get; set; } = true;

    /// <summary>When true, regular (device) users must enroll in MFA.</summary>
    [JsonPropertyName("requireForUser")]
    public bool RequireForUser { get; set; } = false;

    /// <summary>
    /// Number of days after account creation during which MFA enrollment is recommended
    /// but not yet enforced. After this period, login is blocked until MFA is enrolled.
    /// </summary>
    [JsonPropertyName("gracePeriodDays")]
    public int GracePeriodDays { get; set; } = 7;
}

public class ClientSettings
{
    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "https://syntheticgamelabs.dpdns.org";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("syncIntervalMinutes")]
    public int SyncIntervalMinutes { get; set; } = 5;

    [JsonPropertyName("signatureSyncIntervalMinutes")]
    public int SignatureSyncIntervalMinutes { get; set; } = 30;

    [JsonPropertyName("autoSync")]
    public bool AutoSync { get; set; } = true;
}
