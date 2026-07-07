using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Manages cryptographic secrets for the application. Generates and persists a random
/// 64-byte HMAC signing key instead of deriving it predictably from machine identity.
/// The key file is stored at data/hmac_secret.key and created on first run.
/// On Windows, file ACLs are restricted to the current user.
/// </summary>
public static class SecretManager
{
    private const int KeySizeBytes = 64;
    private const string KeyFileName = "hmac_secret.key";

    /// <summary>
    /// Get or create the HMAC signing key for StorageRouter cold-tier integrity.
    /// On first run, generates a cryptographically random 64-byte key and saves it.
    /// On subsequent runs, loads the persisted key from disk.
    /// </summary>
    public static byte[] GetOrCreateHmacKey(string dataDir)
    {
        var keyPath = Path.Combine(dataDir, KeyFileName);

        if (File.Exists(keyPath))
        {
            try
            {
                var key = File.ReadAllBytes(keyPath);
                if (key.Length == KeySizeBytes)
                {
                    SglLogger.Information("[SecretManager] Loaded HMAC key from {Path}", keyPath);
                    SecureKeyFile(keyPath);
                    return key;
                }

                SglLogger.Warning("[SecretManager] HMAC key file has wrong size ({Size}), regenerating", key.Length);
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[SecretManager] Failed to read HMAC key, regenerating: {Error}", ex.Message);
            }
        }

        // Generate new random key
        var newKey = RandomNumberGenerator.GetBytes(KeySizeBytes);

        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllBytes(keyPath, newKey);
            SecureKeyFile(keyPath);
            SglLogger.Information("[SecretManager] Generated and saved new HMAC key to {Path}", keyPath);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecretManager] Could not persist HMAC key: {Error}", ex.Message);
        }

        return newKey;
    }

    /// <summary>
    /// Applies restrictive security to the HMAC key file.
    /// Hides the file on all platforms. On Windows, restricts ACL to current user only.
    /// </summary>
    private static void SecureKeyFile(string keyPath)
    {
        try
        {
            File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var fileInfo = new FileInfo(keyPath);
                    var security = fileInfo.GetAccessControl();

                    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
                    foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
                    {
                        security.RemoveAccessRule(rule);
                    }

                    var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        currentUser,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));

                    fileInfo.SetAccessControl(security);
                    SglLogger.Information("[SecretManager] HMAC key file ACL restricted to {User}", currentUser);
                }
                catch (Exception ex)
                {
                    SglLogger.Warning("[SecretManager] Could not set Windows ACL: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SecretManager] Could not secure key file: {Message}", ex.Message);
        }
    }
}
