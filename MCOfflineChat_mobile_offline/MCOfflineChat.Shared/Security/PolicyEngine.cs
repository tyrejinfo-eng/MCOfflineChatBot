using System.Collections.Concurrent;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Validates automated actions before execution using configurable policy rules.
/// Enforces rate limits, cooldowns, and approval requirements per action per tenant.
/// </summary>
public sealed class PolicyEngine
{
    private readonly ConcurrentDictionary<string, PolicyRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RateTracker> _rateTrackers = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PolicyEngine()
    {
        LoadDefaultPolicies();
    }

    /// <summary>
    /// Validates whether an action is allowed given the current policies and rate limits.
    /// </summary>
    public (bool Allowed, string? Reason) ValidateAction(string action, string role, string? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            return (false, "Action name is required.");

        if (!_rules.TryGetValue(action, out var rule))
        {
            // No rule defined — allow by default
            return (true, null);
        }

        // Check role authorization
        if (rule.AllowedRoles.Count > 0 &&
            !rule.AllowedRoles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)))
        {
            SglLogger.Warning("[PolicyEngine] DENIED: role '{0}' not authorized for action '{1}'", role, action);
            return (false, $"Role '{role}' is not authorized for action '{action}'.");
        }

        // Check if approval is required
        if (rule.RequiresApproval)
        {
            SglLogger.Warning("[PolicyEngine] Action '{0}' requires admin approval", action);
            return (false, $"Action '{action}' requires admin approval before execution.");
        }

        // Check rate limit
        var trackerKey = $"{action}:{tenantId ?? "global"}";
        var tracker = _rateTrackers.GetOrAdd(trackerKey, _ => new RateTracker());

        if (rule.MaxPerHour > 0)
        {
            var count = tracker.GetCountLastHour();
            if (count >= rule.MaxPerHour)
            {
                SglLogger.Warning("[PolicyEngine] Rate limit exceeded for '{0}' — {1}/{2} per hour",
                    action, count, rule.MaxPerHour);
                return (false, $"Rate limit exceeded for '{action}': {count}/{rule.MaxPerHour} per hour.");
            }
        }

        // Check cooldown
        if (rule.CooldownSeconds > 0)
        {
            var lastExecution = tracker.LastExecutionTime;
            if (lastExecution.HasValue)
            {
                var elapsed = (DateTime.UtcNow - lastExecution.Value).TotalSeconds;
                if (elapsed < rule.CooldownSeconds)
                {
                    var remaining = rule.CooldownSeconds - elapsed;
                    SglLogger.Warning("[PolicyEngine] Cooldown active for '{0}' — {1:F0}s remaining",
                        action, remaining);
                    return (false, $"Cooldown active for '{action}': {remaining:F0}s remaining.");
                }
            }
        }

        // All checks passed — record execution
        tracker.RecordExecution();

        SglLogger.Information("[PolicyEngine] Action '{0}' allowed for role '{1}' (tenant={2})",
            action, role, tenantId ?? "global");

        return (true, null);
    }

    /// <summary>
    /// Adds or updates a policy rule.
    /// </summary>
    public void SetRule(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Action))
            throw new ArgumentException("Rule action is required.");

        _rules[rule.Action] = rule;
    }

    /// <summary>
    /// Gets all policy rules.
    /// </summary>
    public List<PolicyRule> GetRules()
    {
        return _rules.Values.ToList();
    }

    /// <summary>
    /// Gets a specific policy rule by action name.
    /// </summary>
    public PolicyRule? GetRule(string action)
    {
        return _rules.TryGetValue(action, out var rule) ? rule : null;
    }

    /// <summary>
    /// Loads policies from a JSON file.
    /// </summary>
    public void LoadPolicies(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                SglLogger.Information("[PolicyEngine] No policy file at {0} — using defaults", path);
                return;
            }

            var json = File.ReadAllText(path);
            var rules = JsonSerializer.Deserialize<List<PolicyRule>>(json, s_jsonOptions);
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Action))
                        _rules[rule.Action] = rule;
                }
            }

            SglLogger.Information("[PolicyEngine] Loaded {0} policy rules from {1}", _rules.Count, path);
        }
        catch (Exception ex)
        {
            SglLogger.Error("[PolicyEngine] Failed to load policies", ex);
        }
    }

    /// <summary>
    /// Saves current policies to a JSON file.
    /// </summary>
    public void SavePolicies(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_rules.Values.ToList(), s_jsonOptions);
            File.WriteAllText(path, json);

            SglLogger.Information("[PolicyEngine] Saved {0} policy rules to {1}", _rules.Count, path);
        }
        catch (Exception ex)
        {
            SglLogger.Error("[PolicyEngine] Failed to save policies", ex);
        }
    }

    /// <summary>
    /// Loads the default built-in policy rules.
    /// </summary>
    private void LoadDefaultPolicies()
    {
        // v1.1.60: Role names aligned with Role.cs PascalCase convention
        _rules["isolate_endpoint"] = new PolicyRule
        {
            Action = "isolate_endpoint",
            RequiresApproval = true,
            AllowedRoles = new List<string> { "Admin", "SOCAnalyst" },
            MaxPerHour = 10,
            CooldownSeconds = 30
        };

        _rules["block_ip"] = new PolicyRule
        {
            Action = "block_ip",
            RequiresApproval = false,
            AllowedRoles = new List<string> { "Admin", "SOCAnalyst", "AutomationOperator" },
            MaxPerHour = 100,
            CooldownSeconds = 5
        };

        _rules["quarantine_file"] = new PolicyRule
        {
            Action = "quarantine_file",
            RequiresApproval = false,
            AllowedRoles = new List<string> { "Admin", "SOCAnalyst", "AutomationOperator" },
            MaxPerHour = 50,
            CooldownSeconds = 2
        };

        _rules["kill_process"] = new PolicyRule
        {
            Action = "kill_process",
            RequiresApproval = false,
            AllowedRoles = new List<string> { "Admin", "AutomationOperator" },
            MaxPerHour = 200,
            CooldownSeconds = 1
        };

        _rules["deploy_update"] = new PolicyRule
        {
            Action = "deploy_update",
            RequiresApproval = true,
            AllowedRoles = new List<string> { "Admin" },
            MaxPerHour = 5,
            CooldownSeconds = 300
        };

        _rules["generate_report"] = new PolicyRule
        {
            Action = "generate_report",
            RequiresApproval = false,
            AllowedRoles = new List<string>(), // all roles
            MaxPerHour = 0, // unlimited
            CooldownSeconds = 0
        };
    }
}

/// <summary>
/// Defines a policy rule for an automated action.
/// </summary>
public class PolicyRule
{
    public string Action { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public List<string> AllowedRoles { get; set; } = new();
    public int MaxPerHour { get; set; }
    public int CooldownSeconds { get; set; }
}

/// <summary>
/// Tracks execution rate for rate limiting and cooldown enforcement.
/// </summary>
internal sealed class RateTracker
{
    private readonly ConcurrentQueue<DateTime> _executions = new();
    public DateTime? LastExecutionTime { get; private set; }

    public void RecordExecution()
    {
        var now = DateTime.UtcNow;
        _executions.Enqueue(now);
        LastExecutionTime = now;

        // Prune entries older than 1 hour
        while (_executions.TryPeek(out var oldest) && (now - oldest).TotalHours > 1)
        {
            _executions.TryDequeue(out _);
        }
    }

    public int GetCountLastHour()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);

        // Prune stale entries
        while (_executions.TryPeek(out var oldest) && oldest < cutoff)
        {
            _executions.TryDequeue(out _);
        }

        return _executions.Count;
    }
}
