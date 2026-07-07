// MCOfflineChat.Shared - Telemetry Encryption at Rest
// AES-256-GCM encryption for telemetry JSONL files
// Copyright (c) 2024-2026 Synthetic Game Labs. All rights reserved.

using System.Security.Cryptography;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// AES-256-GCM encryption for telemetry data at rest.
/// Encrypted format: [12-byte nonce][16-byte tag][ciphertext]
/// Keys are derived from HMAC secrets via HKDF (SHA-256).
/// </summary>
public static class TelemetryEncryptor
{
    private const int NonceSize = 12;  // AES-GCM standard nonce size
    private const int TagSize = 16;    // AES-GCM standard tag size
    private const int KeySize = 32;    // AES-256 key size
    private const int FileBufferSize = 81920;

    /// <summary>
    /// Encrypts plaintext bytes using AES-256-GCM.
    /// Returns byte array: [12-byte nonce][16-byte tag][ciphertext].
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes for AES-256.", nameof(key));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Output format: nonce + tag + ciphertext
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data previously encrypted with <see cref="Encrypt"/>.
    /// Input format: [12-byte nonce][16-byte tag][ciphertext].
    /// </summary>
    public static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes for AES-256.", nameof(key));

        var minLength = NonceSize + TagSize;
        if (encrypted.Length < minLength)
            throw new ArgumentException($"Encrypted data too short. Minimum {minLength} bytes required.", nameof(encrypted));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = encrypted.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encrypted, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(encrypted, NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Encrypts a file using AES-256-GCM. Reads the entire file into memory,
    /// encrypts, and writes to the output path.
    /// </summary>
    public static async Task EncryptFile(string inputPath, string outputPath, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(key);

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input file not found.", inputPath);

        try
        {
            var plaintext = await File.ReadAllBytesAsync(inputPath);
            var encrypted = Encrypt(plaintext, key);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            await File.WriteAllBytesAsync(outputPath, encrypted);

            SglLogger.Information("[TelemetryEncryptor] Encrypted {Input} ({Size} bytes) -> {Output}",
                Path.GetFileName(inputPath), plaintext.Length, Path.GetFileName(outputPath));
        }
        catch (CryptographicException ex)
        {
            SglLogger.Error("[TelemetryEncryptor] Encryption failed for {Path}", ex, inputPath);
            throw;
        }
        catch (IOException ex)
        {
            SglLogger.Error("[TelemetryEncryptor] File I/O error during encryption of {Path}", ex, inputPath);
            throw;
        }
    }

    /// <summary>
    /// Decrypts a file previously encrypted with <see cref="EncryptFile"/>.
    /// </summary>
    public static async Task DecryptFile(string encryptedPath, string outputPath, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(encryptedPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(key);

        if (!File.Exists(encryptedPath))
            throw new FileNotFoundException("Encrypted file not found.", encryptedPath);

        try
        {
            var encrypted = await File.ReadAllBytesAsync(encryptedPath);
            var plaintext = Decrypt(encrypted, key);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            await File.WriteAllBytesAsync(outputPath, plaintext);

            SglLogger.Information("[TelemetryEncryptor] Decrypted {Input} -> {Output} ({Size} bytes)",
                Path.GetFileName(encryptedPath), Path.GetFileName(outputPath), plaintext.Length);
        }
        catch (CryptographicException ex)
        {
            SglLogger.Error("[TelemetryEncryptor] Decryption failed for {Path} — key mismatch or tampered data", ex, encryptedPath);
            throw;
        }
        catch (IOException ex)
        {
            SglLogger.Error("[TelemetryEncryptor] File I/O error during decryption of {Path}", ex, encryptedPath);
            throw;
        }
    }

    /// <summary>
    /// Derives an AES-256 encryption key from an HMAC secret using HKDF (SHA-256).
    /// </summary>
    /// <param name="hmacSecret">The source HMAC key material.</param>
    /// <param name="context">Context string for key derivation (e.g., "telemetry-encryption").</param>
    /// <returns>32-byte AES-256 key.</returns>
    public static byte[] DeriveKeyFromHmac(byte[] hmacSecret, string context = "telemetry-encryption-v1")
    {
        ArgumentNullException.ThrowIfNull(hmacSecret);

        if (hmacSecret.Length == 0)
            throw new ArgumentException("HMAC secret cannot be empty.", nameof(hmacSecret));

        var info = System.Text.Encoding.UTF8.GetBytes(context);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, hmacSecret, KeySize, info);

        return key;
    }
}
