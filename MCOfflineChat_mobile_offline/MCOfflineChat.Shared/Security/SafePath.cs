using System.Security;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Prevents path traversal attacks by validating that resolved paths
/// remain within the expected base directory.
/// </summary>
public static class SafePath
{
    /// <summary>
    /// Resolves and validates a user-supplied path against a base directory.
    /// Throws SecurityException if the resolved path escapes the base.
    /// </summary>
    public static string Resolve(string baseDir, string userPath)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("Base directory cannot be null or empty.", nameof(baseDir));
        if (string.IsNullOrWhiteSpace(userPath))
            throw new ArgumentException("User path cannot be null or empty.", nameof(userPath));

        var normalizedBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
        var resolvedPath = Path.GetFullPath(Path.Combine(baseDir, userPath));

        if (!resolvedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException($"Path traversal attempt detected. Resolved path escapes base directory.");

        return resolvedPath;
    }

    /// <summary>
    /// Returns true if the user path is safe (stays within base), false otherwise.
    /// Does not throw.
    /// </summary>
    public static bool IsSafe(string baseDir, string userPath)
    {
        try
        {
            Resolve(baseDir, userPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
