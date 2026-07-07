using System.Security.Cryptography;
using System.Text;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// HMAC-SHA256 signing and verification for EventBus messages.
/// Each engine gets a unique signing key on registration.
/// EventBus can validate signatures before dispatching events.
/// v1.1.72: Key rotation support — auto-rotates global key every 24h,
/// previous key remains valid for 5 minutes after rotation.
/// </summary>
public sealed class EventBusSigning
{
    private readonly Dictionary<string, byte[]> _engineKeys = new(StringComparer.OrdinalIgnoreCase);
    private byte[] _globalKey;
    private readonly object _lock = new();

    // v1.1.72: Key rotation fields
    private byte[]? _previousKey;
    private DateTime _previousKeyExpiresUtc = DateTime.MinValue;
    private DateTime _lastRotationUtc;
    private static readonly TimeSpan RotationInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan PreviousKeyGracePeriod = TimeSpan.FromMinutes(5);

    public EventBusSigning()
    {
        _globalKey = GenerateKey();
        _lastRotationUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Register an engine and generate a unique signing key for it.
    /// </summary>
    public byte[] RegisterEngine(string engineName)
    {
        var key = GenerateKey();

        lock (_lock)
        {
            _engineKeys[engineName] = key;
        }

        SglLogger.Information("[EventBusSigning] Registered signing key for engine: {0}", engineName);
        return key;
    }

    /// <summary>
    /// Sign a payload using the engine's registered key.
    /// v1.1.72: Auto-rotates the global key if older than 24h.
    /// Returns the HMAC-SHA256 signature as a base64 string.
    /// </summary>
    public string Sign(string engineName, string payload)
    {
        byte[] key;
        lock (_lock)
        {
            // v1.1.72: Auto-rotate global key if stale
            AutoRotateIfNeeded();

            if (!_engineKeys.TryGetValue(engineName, out key!))
                key = _globalKey;
        }

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verify a signature against the engine's registered key.
    /// v1.1.72: Also checks previous global key during grace period after rotation.
    /// </summary>
    public bool Verify(string engineName, string payload, string signature)
    {
        try
        {
            byte[] key;
            byte[]? prevKey = null;

            lock (_lock)
            {
                if (!_engineKeys.TryGetValue(engineName, out key!))
                {
                    key = _globalKey;

                    // v1.1.72: If using global key, also grab previous key if still valid
                    if (_previousKey != null && DateTime.UtcNow < _previousKeyExpiresUtc)
                        prevKey = _previousKey;
                }
            }

            // Check current key first
            using (var hmac = new HMACSHA256(key))
            {
                var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var expected = Convert.FromBase64String(signature);
                if (CryptographicOperations.FixedTimeEquals(computed, expected))
                    return true;
            }

            // v1.1.72: Fall back to previous key during grace period
            if (prevKey != null)
            {
                using var hmacPrev = new HMACSHA256(prevKey);
                var computedPrev = hmacPrev.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var expected = Convert.FromBase64String(signature);
                if (CryptographicOperations.FixedTimeEquals(computedPrev, expected))
                {
                    SglLogger.Information("[EventBusSigning] Verified event using previous (rotated) global key for engine: {0}", engineName);
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sign a TelemetryEvent — creates signature from EventType+Source+Timestamp.
    /// </summary>
    public string SignEvent(Telemetry.TelemetryEvent evt)
    {
        var payload = $"{evt.EventType}|{evt.Source}|{evt.Timestamp:O}|{evt.Severity}";
        return Sign(evt.Source, payload);
    }

    /// <summary>
    /// Verify a TelemetryEvent signature.
    /// </summary>
    public bool VerifyEvent(Telemetry.TelemetryEvent evt, string signature)
    {
        var payload = $"{evt.EventType}|{evt.Source}|{evt.Timestamp:O}|{evt.Severity}";
        return Verify(evt.Source, payload, signature);
    }

    /// <summary>
    /// Get all registered engine names.
    /// </summary>
    public IReadOnlyList<string> GetRegisteredEngines()
    {
        lock (_lock)
        {
            return _engineKeys.Keys.ToList();
        }
    }

    /// <summary>
    /// v1.1.72: Manually rotate the global signing key. The previous key remains valid
    /// for 5 minutes to allow in-flight events signed with it to be verified.
    /// </summary>
    public void RotateKeys()
    {
        lock (_lock)
        {
            _previousKey = _globalKey;
            _previousKeyExpiresUtc = DateTime.UtcNow + PreviousKeyGracePeriod;
            _globalKey = GenerateKey();
            _lastRotationUtc = DateTime.UtcNow;
        }
        SglLogger.Information("[EventBusSigning] Global key rotated. Previous key valid until {0:O}", _previousKeyExpiresUtc);
    }

    /// <summary>
    /// v1.1.72: Returns the UTC timestamp of the last key rotation.
    /// </summary>
    public DateTime LastRotationUtc
    {
        get { lock (_lock) { return _lastRotationUtc; } }
    }

    /// <summary>
    /// v1.1.72: Auto-rotate the global key if it has been more than 24 hours since the last rotation.
    /// Must be called under _lock.
    /// </summary>
    private void AutoRotateIfNeeded()
    {
        if (DateTime.UtcNow - _lastRotationUtc > RotationInterval)
        {
            _previousKey = _globalKey;
            _previousKeyExpiresUtc = DateTime.UtcNow + PreviousKeyGracePeriod;
            _globalKey = GenerateKey();
            _lastRotationUtc = DateTime.UtcNow;
            SglLogger.Information("[EventBusSigning] Auto-rotated global key (>24h). Previous key valid until {0:O}", _previousKeyExpiresUtc);
        }

        // Clean up expired previous key
        if (_previousKey != null && DateTime.UtcNow >= _previousKeyExpiresUtc)
        {
            _previousKey = null;
        }
    }

    /// <summary>Generate a cryptographically random 256-bit key.</summary>
    private static byte[] GenerateKey()
    {
        var key = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return key;
    }
}
