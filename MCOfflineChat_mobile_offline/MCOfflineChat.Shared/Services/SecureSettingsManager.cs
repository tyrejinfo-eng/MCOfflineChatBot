using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Manages loading and saving of arbitrary JSON settings files with automatic
/// DPAPI encryption of sensitive fields (passwords, API keys, tokens, secrets).
/// Works with <see cref="Dictionary{TKey,TValue}"/> key-value settings and
/// complements the strongly-typed <see cref="SecureStorage"/> class.
/// </summary>
public sealed class SecureSettingsManager
{
    private const string DpapiPrefix = "DPAPI:";

    /// <summary>
    /// Field names (case-insensitive) that are considered sensitive and will be
    /// encrypted at rest. Any key whose lowered form contains one of these
    /// substrings is treated as sensitive.
    /// </summary>
    private static readonly string[] SensitiveFieldNames =
    [
        "password",
        "apikey",
        "api_key",
        "secret",
        "token",
        "connectionstring",
        "splunk_token",
        "elastic_password",
        "jwt_secret",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    // ---------------------------------------------------------------
    //  Public API
    // ---------------------------------------------------------------

    /// <summary>
    /// Load a JSON settings file, automatically decrypting any DPAPI-encrypted
    /// sensitive fields. Returns an empty dictionary if the file does not exist
    /// or cannot be parsed.
    /// </summary>
    public Dictionary<string, string> LoadSettings(string path)
    {
        if (!File.Exists(path))
        {
            SglLogger.Warning("[SecureSettingsManager] Settings file not found: {Path}", path);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Rebuild with case-insensitive comparer if needed
            var result = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);

            // Decrypt any encrypted values
            foreach (var key in result.Keys.ToList())
            {
                if (IsEncrypted(result[key]))
                {
                    result[key] = DecryptValue(result[key]);
                }
            }

            SglLogger.Information("[SecureSettingsManager] Loaded settings from {Path} ({Count} keys)", path, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            SglLogger.Error("[SecureSettingsManager] Failed to load settings from {Path}", ex);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Save settings to a JSON file, automatically encrypting any sensitive
    /// fields before writing.
    /// </summary>
    public void SaveSettings(string path, Dictionary<string, string> settings)
    {
        try
        {
            // Work on a copy so we don't mutate the caller's dictionary
            var toWrite = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);

            foreach (var key in toWrite.Keys.ToList())
            {
                if (IsSensitiveField(key) && !IsEncrypted(toWrite[key]))
                {
                    toWrite[key] = EncryptValue(toWrite[key]);
                }
            }

            // Ensure the target directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(toWrite, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);

            SglLogger.Information("[SecureSettingsManager] Saved settings to {Path} ({Count} keys)", path, toWrite.Count);
        }
        catch (Exception ex)
        {
            SglLogger.Error("[SecureSettingsManager] Failed to save settings to {Path}", ex);
        }
    }

    /// <summary>
    /// Encrypt a plaintext value using DPAPI (CurrentUser scope).
    /// Returns <c>"DPAPI:{base64}"</c>. Falls back to returning the plaintext
    /// when DPAPI is unavailable (e.g., on Linux).
    /// </summary>
    public string EncryptValue(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || IsEncrypted(plaintext))
            return plaintext ?? string.Empty;

        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var encryptedBytes = ProtectedData.Protect(
                plaintextBytes, null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(encryptedBytes);
        }
        catch (PlatformNotSupportedException)
        {
            // DPAPI is Windows-only; fall back gracefully on Linux/macOS
            SglLogger.Warning("[SecureSettingsManager] DPAPI not available on this platform — storing value unencrypted");
            return plaintext;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecureSettingsManager] DPAPI encrypt failed: {Error}", ex.Message);
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypt a DPAPI-encrypted <c>"DPAPI:{base64}"</c> string back to
    /// plaintext. Returns the original value if it is not encrypted or if
    /// decryption fails.
    /// </summary>
    public string DecryptValue(string base64Cipher)
    {
        if (string.IsNullOrEmpty(base64Cipher) || !IsEncrypted(base64Cipher))
            return base64Cipher ?? string.Empty;

        try
        {
            var base64 = base64Cipher[DpapiPrefix.Length..];
            var encryptedBytes = Convert.FromBase64String(base64);
            var plaintextBytes = ProtectedData.Unprotect(
                encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (PlatformNotSupportedException)
        {
            SglLogger.Warning("[SecureSettingsManager] DPAPI not available on this platform — returning raw value");
            return base64Cipher;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecureSettingsManager] DPAPI decrypt failed: {Error}", ex.Message);
            return base64Cipher;
        }
    }

    /// <summary>
    /// Check whether a value is already DPAPI-encrypted (starts with the
    /// <c>"DPAPI:"</c> prefix).
    /// </summary>
    public static bool IsEncrypted(string? value)
        => value != null && value.StartsWith(DpapiPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Migrate an existing unencrypted settings file in-place: reads all
    /// key-value pairs, encrypts any sensitive fields that are not yet
    /// encrypted, and writes the file back.
    /// </summary>
    public void MigrateSettings(string path)
    {
        if (!File.Exists(path))
        {
            SglLogger.Warning("[SecureSettingsManager] Cannot migrate — file not found: {Path}", path);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var migrated = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            var count = 0;

            foreach (var key in migrated.Keys.ToList())
            {
                if (IsSensitiveField(key) && !string.IsNullOrEmpty(migrated[key]) && !IsEncrypted(migrated[key]))
                {
                    migrated[key] = EncryptValue(migrated[key]);
                    count++;
                }
            }

            if (count > 0)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var output = JsonSerializer.Serialize(migrated, JsonOptions);
                File.WriteAllText(path, output, Encoding.UTF8);
                SglLogger.Information("[SecureSettingsManager] Migrated {Count} sensitive fields in {Path}", count, path);
            }
            else
            {
                SglLogger.Information("[SecureSettingsManager] No unencrypted sensitive fields found in {Path}", path);
            }
        }
        catch (Exception ex)
        {
            SglLogger.Error("[SecureSettingsManager] Migration failed for {Path}", ex);
        }
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Determines whether a settings key name refers to a sensitive value
    /// (password, API key, token, etc.).
    /// </summary>
    private static bool IsSensitiveField(string key)
    {
        var lower = key.ToLowerInvariant();
        foreach (var sensitive in SensitiveFieldNames)
        {
            if (lower.Contains(sensitive, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
