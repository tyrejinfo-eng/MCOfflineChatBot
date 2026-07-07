// MCOfflineChat.Shared - Native DLL Integrity Verification
// Generates and verifies SHA-256 manifests for native runtime libraries
// to detect tampering or corruption of shipped native binaries.
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Helpers;

/// <summary>
/// Generates and verifies SHA-256 integrity manifests for native DLLs
/// in the runtimes directory. Used to detect tampering or corruption
/// of shipped native binaries (e.g. LLamaSharp, CUDA, ONNX runtime).
/// <para>
/// When <see cref="FailHardOnMismatch"/> is set to <c>true</c>, the
/// <see cref="VerifyIntegrity"/> method will throw a <see cref="SecurityException"/>
/// if any integrity violations are detected, preventing the application from
/// running with potentially tampered native binaries. This is intended for
/// production deployments where security is paramount.
/// </para>
/// </summary>
public static class NativeDllIntegrity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// When set to <c>true</c>, <see cref="VerifyIntegrity"/> will throw a
    /// <see cref="SecurityException"/> if any integrity violations (missing files
    /// or hash mismatches) are detected. Default is <c>false</c> (warn-only mode).
    /// <para>
    /// Configure this via <c>SecuritySettings.NativeDllFailHard</c> in settings JSON,
    /// or set directly before calling verification methods. When enabled, the application
    /// will refuse to start with tampered or missing native binaries.
    /// </para>
    /// </summary>
    public static bool FailHardOnMismatch { get; set; } = false;

    /// <summary>
    /// Compute SHA-256 hashes of all .dll and .so files recursively in the given directory.
    /// Returns a dictionary mapping relative path (forward-slash separated) to hex-encoded hash.
    /// </summary>
    public static Dictionary<string, string> GenerateManifest(string runtimesDir)
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(runtimesDir))
        {
            SglLogger.Warning("NativeDllIntegrity: runtimes directory does not exist: {Dir}", runtimesDir);
            return manifest;
        }

        var files = Directory.EnumerateFiles(runtimesDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".so", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            try
            {
                var hash = ComputeSha256(file);
                var relativePath = Path.GetRelativePath(runtimesDir, file)
                    .Replace('\\', '/');
                manifest[relativePath] = hash;
            }
            catch (Exception ex)
            {
                SglLogger.Error("NativeDllIntegrity: failed to hash {File}", ex, file);
            }
        }

        SglLogger.Information("NativeDllIntegrity: generated manifest with {Count} entries", manifest.Count);
        return manifest;
    }

    /// <summary>
    /// Save a manifest dictionary as JSON to the specified output path.
    /// Creates the parent directory if it does not exist.
    /// </summary>
    public static void SaveManifest(Dictionary<string, string> manifest, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(outputPath, json);
        SglLogger.Information("NativeDllIntegrity: saved manifest to {Path} ({Count} entries)", outputPath, manifest.Count);
    }

    /// <summary>
    /// Load a previously saved manifest from a JSON file.
    /// Returns an empty dictionary if the file does not exist or is invalid.
    /// </summary>
    public static Dictionary<string, string> LoadManifest(string path)
    {
        if (!File.Exists(path))
        {
            SglLogger.Warning("NativeDllIntegrity: manifest file not found: {Path}", path);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return manifest != null
                ? new Dictionary<string, string>(manifest, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SglLogger.Error("NativeDllIntegrity: failed to load manifest from {Path}", ex, path);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Compare current hashes of native binaries against a saved manifest.
    /// Returns a list of mismatched or missing files. An empty list means all files match.
    /// If the manifest file does not exist, logs a warning and returns an empty list (no crash).
    /// <para>
    /// When <see cref="FailHardOnMismatch"/> is <c>true</c> and violations are found,
    /// this method logs a CRITICAL message and throws <see cref="SecurityException"/>
    /// to prevent the application from running with tampered native binaries.
    /// </para>
    /// </summary>
    /// <exception cref="SecurityException">
    /// Thrown when <see cref="FailHardOnMismatch"/> is <c>true</c> and one or more
    /// integrity violations are detected.
    /// </exception>
    public static List<string> VerifyIntegrity(string runtimesDir, string manifestPath)
    {
        var violations = new List<string>();

        if (!File.Exists(manifestPath))
        {
            SglLogger.Warning("NativeDllIntegrity: manifest not found at {Path}, skipping integrity verification", manifestPath);
            return violations;
        }

        var expected = LoadManifest(manifestPath);
        if (expected.Count == 0)
        {
            SglLogger.Warning("NativeDllIntegrity: manifest is empty, skipping integrity verification");
            return violations;
        }

        var current = GenerateManifest(runtimesDir);

        // Check every file in the manifest
        foreach (var (relativePath, expectedHash) in expected)
        {
            if (!current.TryGetValue(relativePath, out var currentHash))
            {
                violations.Add($"MISSING: {relativePath}");
                SglLogger.Warning("NativeDllIntegrity: file missing from runtimes: {File}", relativePath);
            }
            else if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"MISMATCH: {relativePath} (expected {expectedHash[..12]}..., got {currentHash[..12]}...)");
                SglLogger.Warning("NativeDllIntegrity: hash mismatch for {File}", relativePath);
            }
        }

        if (violations.Count == 0)
        {
            SglLogger.Information("NativeDllIntegrity: all {Count} native binaries passed integrity check", expected.Count);
        }
        else
        {
            SglLogger.Warning("NativeDllIntegrity: {Count} integrity violations detected", violations.Count);

            if (FailHardOnMismatch)
            {
                SglLogger.Warning("NativeDllIntegrity: CRITICAL - FailHardOnMismatch is enabled. Aborting due to {Count} violations: {Violations}",
                    violations.Count, string.Join("; ", violations));
                throw new SecurityException("Native DLL integrity check failed. Possible tampering detected.");
            }
        }

        return violations;
    }

    /// <summary>
    /// Strict verification that returns <c>false</c> on ANY integrity violation
    /// (missing files or hash mismatches), without throwing an exception.
    /// <para>
    /// Unlike <see cref="VerifyIntegrity"/>, this method is not affected by
    /// <see cref="FailHardOnMismatch"/>. It always returns a boolean result,
    /// making it suitable for conditional checks where the caller wants to
    /// decide how to handle violations.
    /// </para>
    /// </summary>
    /// <param name="runtimesDir">Path to the runtimes directory containing native binaries.</param>
    /// <param name="manifestPath">Path to the JSON manifest file with expected hashes.</param>
    /// <returns><c>true</c> if all files match the manifest; <c>false</c> if any violation is found
    /// or the manifest is missing/empty.</returns>
    public static bool VerifyIntegrityStrict(string runtimesDir, string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            SglLogger.Warning("NativeDllIntegrity.Strict: manifest not found at {Path}, failing strict check", manifestPath);
            return false;
        }

        var expected = LoadManifest(manifestPath);
        if (expected.Count == 0)
        {
            SglLogger.Warning("NativeDllIntegrity.Strict: manifest is empty, failing strict check");
            return false;
        }

        var current = GenerateManifest(runtimesDir);

        foreach (var (relativePath, expectedHash) in expected)
        {
            if (!current.TryGetValue(relativePath, out var currentHash))
            {
                SglLogger.Warning("NativeDllIntegrity.Strict: file missing from runtimes: {File}", relativePath);
                return false;
            }

            if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                SglLogger.Warning("NativeDllIntegrity.Strict: hash mismatch for {File}", relativePath);
                return false;
            }
        }

        SglLogger.Information("NativeDllIntegrity.Strict: all {Count} native binaries passed strict integrity check", expected.Count);
        return true;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
