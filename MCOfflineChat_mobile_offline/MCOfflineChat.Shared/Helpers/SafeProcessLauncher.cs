// MCOfflineChat.Shared - Safe Process Launcher
// Validates and logs all process launches through an allowlisted executable set.
// Prevents command injection by blocking dangerous shell metacharacters in arguments.
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

#pragma warning disable CA1416 // Platform compatibility - this is a Windows-only application

using System.Diagnostics;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Helpers;

/// <summary>
/// Security wrapper for Process.Start that enforces an allowlist of permitted executables
/// and validates arguments against command injection characters.
/// All launch attempts are logged for audit purposes.
/// </summary>
public static class SafeProcessLauncher
{
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "notepad.exe", "cmd.exe", "powershell.exe",
        "powershell",
        "git", "git.exe",
        "dotnet", "dotnet.exe",
        "netstat", "netstat.exe",
        "netsh", "netsh.exe",
        "net", "net.exe",
        "ffmpeg", "ffmpeg.exe",
        "ffplay", "ffplay.exe",
        "python", "python.exe",
        "pip", "pip.exe",
        "cloudflared", "cloudflared.exe", "cloudflared-windows-amd64.exe",
        "where", "where.exe",
        "msiexec", "msiexec.exe",
        "ServiceController"
    };

    /// <summary>Dangerous shell metacharacters that enable command chaining/injection.</summary>
    private static readonly char[] DangerousChars = { '|', '&', '`', ';', '$', '<', '>' };

    /// <summary>
    /// Launch a process after validating the executable is allowlisted and arguments are safe.
    /// Returns null if the launch is denied (does not throw).
    /// </summary>
    /// <param name="executable">The executable name or full path. Only the filename portion is checked against the allowlist.</param>
    /// <param name="arguments">Command-line arguments. Checked for dangerous shell metacharacters.</param>
    /// <param name="redirectOutput">When true, redirects stdout, stderr, and stdin for programmatic access.</param>
    /// <param name="workingDirectory">Optional working directory for the launched process.</param>
    public static Process? Launch(string executable, string arguments, bool redirectOutput = false, string? workingDirectory = null)
    {
        var fileName = Path.GetFileName(executable);

        if (!AllowedExecutables.Contains(fileName))
        {
            SglLogger.Warning("[SafeProcessLauncher] BLOCKED: executable '{Executable}' (resolved: '{FileName}') is not on the allowlist",
                executable, fileName);
            return null;
        }

        // Validate arguments for dangerous shell metacharacters.
        // Exception: netsh is allowed to use '>' for output redirection.
        var isNetsh = fileName.Equals("netsh", StringComparison.OrdinalIgnoreCase) ||
                      fileName.Equals("netsh.exe", StringComparison.OrdinalIgnoreCase);

        foreach (var c in DangerousChars)
        {
            if (c == '>' && isNetsh) continue;
            if (arguments.IndexOf(c) >= 0)
            {
                SglLogger.Warning("[SafeProcessLauncher] BLOCKED: dangerous character '{Char}' in arguments for '{Executable}'",
                    c, executable);
                return null;
            }
        }

        SglLogger.Information("[SafeProcessLauncher] Launching: {Executable} {Arguments} (redirect={Redirect})",
            executable, arguments, redirectOutput);

        try
        {
            var psi = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (workingDirectory != null)
                psi.WorkingDirectory = workingDirectory;

            if (redirectOutput)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
            }

            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SafeProcessLauncher] Failed to start '{Executable}': {Message}",
                executable, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Launch the current process with UAC elevation (runas verb).
    /// Used for self-elevation of the running application.
    /// Requires UseShellExecute=true for the UAC prompt, so this is a separate method
    /// from <see cref="Launch"/> which always uses UseShellExecute=false.
    /// </summary>
    /// <param name="executable">Full path to the executable to elevate.</param>
    public static Process? LaunchElevated(string executable)
    {
        SglLogger.Information("[SafeProcessLauncher] Elevated launch (runas) requested: {Executable}", executable);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            };

            return Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt
            SglLogger.Warning("[SafeProcessLauncher] UAC elevation declined by user for '{Executable}'", executable);
            return null;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SafeProcessLauncher] Elevated launch failed for '{Executable}': {Message}",
                executable, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Validates that the given executable is on the allowlist without launching a process.
    /// Returns true if the executable is permitted, false otherwise.
    /// Use this before calling Process.Start directly when the Process object must be
    /// created and managed externally (e.g., for long-running monitored processes).
    /// </summary>
    /// <param name="executable">The executable name or full path. Only the filename portion is checked.</param>
    public static bool ValidateExecutable(string executable)
    {
        var fileName = Path.GetFileName(executable);

        if (AllowedExecutables.Contains(fileName))
            return true;

        SglLogger.Warning("[SafeProcessLauncher] VALIDATE FAILED: executable '{Executable}' (resolved: '{FileName}') is not on the allowlist",
            executable, fileName);
        return false;
    }

    /// <summary>
    /// Validates that the given arguments do not contain dangerous shell metacharacters.
    /// Returns true if the arguments are safe, false otherwise.
    /// Use this before calling Process.Start directly when the Process object must be
    /// created and managed externally.
    /// </summary>
    /// <param name="executable">The executable name (used for netsh exemption on '>').</param>
    /// <param name="arguments">The arguments string to validate.</param>
    public static bool ValidateArguments(string executable, string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return true;

        var fileName = Path.GetFileName(executable);
        var isNetsh = fileName.Equals("netsh", StringComparison.OrdinalIgnoreCase) ||
                      fileName.Equals("netsh.exe", StringComparison.OrdinalIgnoreCase);

        foreach (var c in DangerousChars)
        {
            if (c == '>' && isNetsh) continue;
            if (arguments.IndexOf(c) >= 0)
            {
                SglLogger.Warning("[SafeProcessLauncher] VALIDATE FAILED: dangerous character '{Char}' in arguments for '{Executable}'",
                    c, executable);
                return false;
            }
        }

        return true;
    }
}
