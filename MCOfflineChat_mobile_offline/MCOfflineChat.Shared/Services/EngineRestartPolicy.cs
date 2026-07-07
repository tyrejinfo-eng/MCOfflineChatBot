using System.Collections.Concurrent;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// v1.1.58: Manages engine restart decisions with rate limiting and exponential backoff.
/// Max 3 restart attempts per engine before giving up, with 5-minute cooldown after max.
/// Backoff: 1s, 2s, 4s (base * 2^attempt).
/// </summary>
public sealed class EngineRestartPolicy
{
    private const int MaxRestartAttempts = 3;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, RestartPolicyState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the engine should be restarted, false if max attempts
    /// reached and cooldown has not elapsed.
    /// </summary>
    public bool ShouldRestart(string engineName)
    {
        var state = _states.GetOrAdd(engineName, _ => new RestartPolicyState());

        // If max attempts reached, check cooldown
        if (state.Attempts >= MaxRestartAttempts)
        {
            if (state.MaxAttemptsReachedAt.HasValue &&
                (DateTime.UtcNow - state.MaxAttemptsReachedAt.Value) < CooldownPeriod)
            {
                SglLogger.Warning("[RestartPolicy] {Engine} in cooldown ({Remaining:F0}s remaining)",
                    engineName,
                    (CooldownPeriod - (DateTime.UtcNow - state.MaxAttemptsReachedAt.Value)).TotalSeconds);
                return false;
            }

            // Cooldown elapsed — reset and allow retry
            state.Attempts = 0;
            state.MaxAttemptsReachedAt = null;
        }

        return true;
    }

    /// <summary>
    /// Records a restart attempt. Returns the backoff delay before the restart should proceed.
    /// </summary>
    public TimeSpan RecordRestart(string engineName)
    {
        var state = _states.GetOrAdd(engineName, _ => new RestartPolicyState());
        state.Attempts++;
        state.LastAttempt = DateTime.UtcNow;
        state.TotalAttempts++;

        if (state.Attempts >= MaxRestartAttempts)
            state.MaxAttemptsReachedAt = DateTime.UtcNow;

        // Exponential backoff: 1s * 2^(attempt-1) = 1s, 2s, 4s
        var backoff = BaseBackoff * Math.Pow(2, state.Attempts - 1);
        SglLogger.Information("[RestartPolicy] Recorded restart #{Attempt} for {Engine}, backoff={Backoff:F1}s",
            state.Attempts, engineName, backoff.TotalSeconds);
        return backoff;
    }

    /// <summary>
    /// Records a successful engine start, resetting the attempt counter.
    /// </summary>
    public void RecordSuccess(string engineName)
    {
        var state = _states.GetOrAdd(engineName, _ => new RestartPolicyState());
        state.Attempts = 0;
        state.MaxAttemptsReachedAt = null;
        state.LastSuccess = DateTime.UtcNow;
        SglLogger.Information("[RestartPolicy] {Engine} started successfully, counter reset", engineName);
    }

    /// <summary>Get current state for an engine.</summary>
    public RestartPolicyState? GetState(string engineName)
    {
        return _states.TryGetValue(engineName, out var s) ? s : null;
    }
}

/// <summary>Tracks restart attempt state for a single engine.</summary>
public class RestartPolicyState
{
    public int Attempts { get; set; }
    public long TotalAttempts { get; set; }
    public DateTime? LastAttempt { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? MaxAttemptsReachedAt { get; set; }
}
