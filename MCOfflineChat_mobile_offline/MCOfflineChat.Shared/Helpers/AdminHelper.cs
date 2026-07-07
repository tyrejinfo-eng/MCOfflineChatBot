#pragma warning disable CA1416 // Platform compatibility - this is a Windows-only application
using System.Security.Principal;

namespace MCOfflineChat.Shared.Helpers;

public static class AdminHelper
{
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Attempts to restart the current process with admin elevation.
    /// Returns true if the elevated process was successfully started.
    /// Returns false if elevation failed (UAC declined, path unavailable, etc.).
    /// </summary>
    public static bool TryRestartAsAdmin()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        var process = SafeProcessLauncher.LaunchElevated(exePath);
        return process != null;
    }

    [Obsolete("Use TryRestartAsAdmin() instead for proper error feedback")]
    public static void RestartAsAdmin()
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        SafeProcessLauncher.LaunchElevated(exePath);
    }
}
