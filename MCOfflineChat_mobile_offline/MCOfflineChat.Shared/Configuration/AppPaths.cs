// MCOfflineChat.Shared - Application Path Resolution
// Fixes critical bug: AppContext.BaseDirectory returns single-file extraction temp dir,
// NOT the actual install directory. This class returns the real EXE directory so settings,
// data, logs, and models are found at the correct installed location.
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

namespace MCOfflineChat.Shared.Configuration;

/// <summary>
/// Provides correct filesystem paths for the application, accounting for
/// single-file publish extraction behavior in .NET 8.
/// <para>
/// When published with <c>PublishSingleFile=true</c> and
/// <c>IncludeAllContentForSelfExtract=true</c>, <see cref="AppContext.BaseDirectory"/>
/// returns the temp extraction directory (e.g. %LOCALAPPDATA%\Temp\.net\...) rather than
/// the directory where the EXE actually lives on disk. This class always returns the
/// real EXE directory via <see cref="Environment.ProcessPath"/>.
/// </para>
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _installDir = new(() =>
    {
        // Environment.ProcessPath gives the full path to the running EXE file.
        // For single-file published apps, this is the REAL location on disk (e.g. C:\Program Files\...)
        // NOT the temp extraction directory.
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(dir))
                return dir;
        }

        // Fallback: try the main module path
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            var mainModule = proc.MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainModule))
            {
                var dir = Path.GetDirectoryName(mainModule);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
        }
        catch { /* security exception in some environments */ }

        // Ultimate fallback
        return AppContext.BaseDirectory;
    });

    /// <summary>
    /// The REAL directory where the application EXE lives on disk.
    /// For installed apps: C:\Program Files\MC Offline Chat Server\
    /// NOT the single-file extraction temp directory.
    /// </summary>
    public static string InstallDir => _installDir.Value;

    /// <summary>Application data directory (settings, secrets, blacklists).</summary>
    public static string DataDir => Path.Combine(InstallDir, "data");

    /// <summary>Log output directory.</summary>
    public static string LogDir => Path.Combine(InstallDir, "logs");

    /// <summary>LLM model directory (server inference models).</summary>
    public static string LlmServerDir => Path.Combine(InstallDir, "LLM_Server");

    /// <summary>LLM model directory (client download models).</summary>
    public static string LlmClientDir => Path.Combine(InstallDir, "LLM");

    /// <summary>Website files directory.</summary>
    public static string WebsiteDir => Path.Combine(InstallDir, "data", "website");

    /// <summary>SD WebUI directory.</summary>
    public static string SdWebuiDir => Path.Combine(InstallDir, "sd_webui");

    /// <summary>TTS engine directory.</summary>
    public static string TtsDir => Path.Combine(InstallDir, "tts");

    /// <summary>Resolve a path relative to the install directory.</summary>
    public static string Resolve(params string[] segments) =>
        Path.Combine(new[] { InstallDir }.Concat(segments).ToArray());
}
