namespace MCOfflineChat.Shared.Logging;

using System.Text.RegularExpressions;

/// <summary>
/// v1.1.72: Structured log field redaction. Prevents sensitive data from being written to log files.
/// Redacts passwords, tokens, API keys, credit card numbers, and other PII from log messages.
/// </summary>
public static partial class LogRedactor
{
    // Patterns to redact (compiled for performance)
    private static readonly (Regex Pattern, string Replacement)[] RedactionRules =
    [
        // Passwords in key=value or JSON
        (PasswordPattern(), "[password=REDACTED]"),
        // Bearer tokens
        (BearerPattern(), "Bearer [REDACTED]"),
        // API keys (common formats: 32+ hex/base64 chars)
        (ApiKeyPattern(), "$1[REDACTED]"),
        // JWT tokens (three dot-separated base64 segments)
        (JwtPattern(), "[JWT-REDACTED]"),
        // Credit card numbers (4 groups of 4 digits)
        (CreditCardPattern(), "[CC-REDACTED]"),
        // Email addresses in unexpected contexts
        (EmailInLogPattern(), "[EMAIL-REDACTED]"),
        // Connection strings with passwords
        (ConnStringPasswordPattern(), "Password=[REDACTED]"),
        // Base64-encoded secrets (long base64 strings preceded by key-like identifiers)
        (Base64SecretPattern(), "$1[REDACTED]"),
    ];

    /// <summary>Redact sensitive fields from a log message.</summary>
    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var result = message;
        foreach (var (pattern, replacement) in RedactionRules)
        {
            result = pattern.Replace(result, replacement);
        }
        return result;
    }

    /// <summary>Redact command-line arguments that may contain secrets.</summary>
    public static string RedactCommandLine(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return string.Empty;

        // Redact common secret flags
        var result = commandLine;
        result = ArgPasswordPattern().Replace(result, "$1 [REDACTED]");
        result = ArgTokenPattern().Replace(result, "$1 [REDACTED]");
        result = ArgKeyPattern().Replace(result, "$1 [REDACTED]");
        return result;
    }

    [GeneratedRegex(@"(?:password|passwd|pwd|secret)\s*[=:]\s*\S+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"((?:api[_-]?key|apikey|x-api-key)\s*[=:]\s*)\S+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailInLogPattern();

    [GeneratedRegex(@"Password\s*=\s*[^;]+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ConnStringPasswordPattern();

    [GeneratedRegex(@"((?:secret|token|key)\s*[=:]\s*)[A-Za-z0-9+/]{32,}=*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex Base64SecretPattern();

    [GeneratedRegex(@"(-{1,2}(?:password|passwd|pwd))\s+\S+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ArgPasswordPattern();

    [GeneratedRegex(@"(-{1,2}(?:token|access[-_]?token))\s+\S+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ArgTokenPattern();

    [GeneratedRegex(@"(-{1,2}(?:api[-_]?key|secret[-_]?key))\s+\S+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ArgKeyPattern();
}
