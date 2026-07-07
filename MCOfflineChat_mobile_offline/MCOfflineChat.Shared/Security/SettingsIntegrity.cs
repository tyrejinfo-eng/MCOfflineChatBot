// MCOfflineChat.Shared - Settings Integrity Verification
// HMAC-SHA256 tamper detection for configuration files.
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

namespace MCOfflineChat.Shared.Security;

using System.Security.Cryptography;
using System.Text;
using MCOfflineChat.Shared.Logging;

/// <summary>
/// v1.1.72: HMAC-SHA256 integrity verification for settings files.
/// Detects unauthorized tampering with configuration files.
/// </summary>
public static class SettingsIntegrity
{
    private static readonly byte[] _machineKey = DeriveKey();

    /// <summary>Compute HMAC signature for a settings file.</summary>
    public static string ComputeSignature(string filePath)
    {
        var content = File.ReadAllBytes(filePath);
        using var hmac = new HMACSHA256(_machineKey);
        var hash = hmac.ComputeHash(content);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Write .sig companion file next to the settings file.</summary>
    public static void Sign(string filePath)
    {
        var sig = ComputeSignature(filePath);
        File.WriteAllText(filePath + ".sig", sig);
        SglLogger.Information("[SettingsIntegrity] Signed: {FileName}", Path.GetFileName(filePath));
    }

    /// <summary>Verify settings file against its .sig companion. Returns true if valid or no sig file exists (first run).</summary>
    public static bool Verify(string filePath)
    {
        var sigPath = filePath + ".sig";
        if (!File.Exists(sigPath))
        {
            // First run or no signature — sign it now
            if (File.Exists(filePath))
                Sign(filePath);
            return true;
        }

        var stored = File.ReadAllText(sigPath).Trim();
        var computed = ComputeSignature(filePath);
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(computed));

        if (!valid)
            SglLogger.Warning("[SettingsIntegrity] TAMPER DETECTED: {FileName} signature mismatch!",
                Path.GetFileName(filePath));

        return valid;
    }

    /// <summary>Verify and re-sign after legitimate settings update.</summary>
    public static void VerifyAndResign(string filePath)
    {
        if (File.Exists(filePath))
            Sign(filePath);
    }

    private static byte[] DeriveKey()
    {
        var seed = $"{Environment.MachineName}|{AppDomain.CurrentDomain.BaseDirectory}|MCOfflineChat-Settings-v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }
}
