namespace MCOfflineChat.Core.Models;

/// <summary>
/// Defines the role a node plays in the multi-tenant hierarchy.
/// </summary>
public enum SystemRole
{
    /// <summary>Central MCOfflineChat cloud server that manages all company tenants.</summary>
    MainServer,

    /// <summary>Company-level server that manages its own devices.</summary>
    CompanyServer,

    /// <summary>End-user device running the antivirus client.</summary>
    Client
}

/// <summary>
/// License tier controlling feature gates and resource limits.
/// </summary>
public enum LicenseTier
{
    Free,
    Standard,
    Enterprise
}

/// <summary>
/// Represents a company tenant in the multi-tenant hierarchy.
/// Each tenant has isolated policies, devices, and telemetry.
/// </summary>
public sealed class Tenant
{
    /// <summary>Unique tenant identifier (GUID string).</summary>
    public string TenantId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name of the company.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>When the tenant was provisioned.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the tenant is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Maximum number of enrolled devices allowed.</summary>
    public int MaxDevices { get; set; } = 100;

    /// <summary>Maximum number of user accounts allowed.</summary>
    public int MaxUsers { get; set; } = 10;

    /// <summary>Feature tier for this tenant.</summary>
    public LicenseTier LicenseTier { get; set; } = LicenseTier.Free;
}

/// <summary>
/// Represents an enrolled device (endpoint) within a tenant.
/// Tracks identity, location, and heartbeat information.
/// </summary>
public sealed class DeviceNode
{
    /// <summary>Unique device identifier (GUID string).</summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The tenant this device belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Machine hostname.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Operating system version string.</summary>
    public string OsVersion { get; set; } = string.Empty;

    /// <summary>Last heartbeat timestamp.</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the device is currently connected.</summary>
    public bool IsOnline { get; set; }

    /// <summary>GPS latitude if available (mobile / opt-in).</summary>
    public double? GpsLatitude { get; set; }

    /// <summary>GPS longitude if available (mobile / opt-in).</summary>
    public double? GpsLongitude { get; set; }

    /// <summary>When the device was first enrolled.</summary>
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-tenant security policy controlling what features are enabled
/// and resource limits for telemetry collection.
/// </summary>
public sealed class TenantPolicy
{
    /// <summary>The tenant this policy applies to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Whether remote shutdown commands are allowed.</summary>
    public bool AllowShutdown { get; set; }

    /// <summary>Whether tamper protection is enforced on endpoints.</summary>
    public bool TamperProtection { get; set; } = true;

    /// <summary>Whether screen monitoring is enabled.</summary>
    public bool ScreenMonitoring { get; set; }

    /// <summary>How many days to retain telemetry data.</summary>
    public int DataRetentionDays { get; set; } = 90;

    /// <summary>Maximum telemetry events ingested per day per device.</summary>
    public long MaxEventsPerDay { get; set; } = 100_000;
}
