using System.Security.Cryptography;
using System.Text;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Provides DPAPI-based encryption for sensitive settings values (API keys, passwords, tokens).
/// Uses DataProtectionScope.CurrentUser so only the same Windows user can decrypt.
/// Encrypted values are prefixed with "ENC:" followed by Base64 so they can be detected
/// automatically when loading settings from JSON.
/// </summary>
public static class SecureStorage
{
    private const string EncPrefix = "ENC:";

    /// <summary>Check if a value is already DPAPI-encrypted (has the ENC: prefix).</summary>
    public static bool IsEncrypted(string? value)
        => value != null && value.StartsWith(EncPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Encrypt a plaintext string using DPAPI (CurrentUser scope).
    /// Returns "ENC:{base64}" string. Returns the original value if already encrypted or empty.
    /// </summary>
    public static string Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || IsEncrypted(plaintext))
            return plaintext ?? string.Empty;

        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var encryptedBytes = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);
            return EncPrefix + Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecureStorage] DPAPI encrypt failed (non-Windows?): {Error}", ex.Message);
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypt a DPAPI-encrypted "ENC:{base64}" string back to plaintext.
    /// Returns the original value if not encrypted or decryption fails.
    /// </summary>
    public static string Decrypt(string? encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue) || !IsEncrypted(encryptedValue))
            return encryptedValue ?? string.Empty;

        try
        {
            var base64 = encryptedValue[EncPrefix.Length..];
            var encryptedBytes = Convert.FromBase64String(base64);
            var plaintextBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecureStorage] DPAPI decrypt failed: {Error}", ex.Message);
            return encryptedValue;
        }
    }

    /// <summary>
    /// Encrypt all sensitive fields in AppSettings before saving to disk.
    /// Only encrypts non-empty, non-already-encrypted values.
    /// </summary>
    public static void EncryptSettings(Configuration.AppSettings settings)
    {
        if (!OperatingSystem.IsWindows()) return;

        settings.Server.CloudflareApiKey = Encrypt(settings.Server.CloudflareApiKey);
        settings.Server.AdminPassword = Encrypt(settings.Server.AdminPassword);
        settings.Server.CertificatePassword = Encrypt(settings.Server.CertificatePassword);
        settings.Server.TunnelToken = Encrypt(settings.Server.TunnelToken);
        settings.Client.ApiKey = Encrypt(settings.Client.ApiKey);
    }

    /// <summary>
    /// Decrypt all sensitive fields in AppSettings after loading from disk.
    /// Non-encrypted values pass through unchanged.
    /// </summary>
    public static void DecryptSettings(Configuration.AppSettings settings)
    {
        if (!OperatingSystem.IsWindows()) return;

        settings.Server.CloudflareApiKey = Decrypt(settings.Server.CloudflareApiKey);
        settings.Server.AdminPassword = Decrypt(settings.Server.AdminPassword);
        settings.Server.CertificatePassword = Decrypt(settings.Server.CertificatePassword);
        settings.Server.TunnelToken = Decrypt(settings.Server.TunnelToken);
        settings.Client.ApiKey = Decrypt(settings.Client.ApiKey);
    }
}
