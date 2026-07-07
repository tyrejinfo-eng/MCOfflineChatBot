namespace MCOfflineChat.Mobile.Models;

/// <summary>
/// Represents a single threat intelligence signal shared through the swarm network.
/// </summary>
public class SwarmSignalItem
{
    public string SignalId { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>"Sent" or "Received"</summary>
    public string Direction { get; set; } = "Received";

    // ── Derived display helpers ──────────────────────────────────────────────

    /// <summary>SignalId truncated to 8 chars for display.</summary>
    public string ShortId => SignalId.Length > 8 ? SignalId[..8] : SignalId;

    /// <summary>Pattern truncated to 40 chars for display.</summary>
    public string ShortPattern => Pattern.Length > 40 ? Pattern[..40] + "…" : Pattern;

    /// <summary>Score as a percentage string, e.g. "75%".</summary>
    public string ScoreDisplay => $"{Score * 100:F0}%";

    /// <summary>Hex color for the score badge: red ≥ 0.8, yellow ≥ 0.5, green otherwise.</summary>
    public string ScoreColor => Score >= 0.8 ? "#F38BA8"
        : Score >= 0.5 ? "#F9E2AF"
        : "#A6E3A1";

    /// <summary>Arrow character indicating signal direction.</summary>
    public string DirectionArrow => Direction == "Sent" ? "↑" : "↓";

    /// <summary>Color for direction arrow: cyan for sent, muted for received.</summary>
    public string DirectionColor => Direction == "Sent" ? "#89DCEB" : "#A0A0A0";
}
