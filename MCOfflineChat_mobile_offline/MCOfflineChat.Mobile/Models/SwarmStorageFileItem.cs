namespace MCOfflineChat.Mobile.Models;

/// <summary>
/// Represents a file stored in the distributed swarm storage system.
/// </summary>
public class SwarmStorageFileItem
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Uploader { get; set; } = string.Empty;
    public string UploadedAt { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    // ── Derived display helpers ──────────────────────────────────────────────

    /// <summary>FileId truncated to 8 chars for display.</summary>
    public string ShortId => FileId.Length > 8 ? FileId[..8] : FileId;

    /// <summary>File size formatted as B / KB / MB.</summary>
    public string SizeDisplay => SizeBytes >= 1_048_576
        ? $"{SizeBytes / 1_048_576.0:F1} MB"
        : SizeBytes >= 1024
            ? $"{SizeBytes / 1024.0:F1} KB"
            : $"{SizeBytes} B";

    /// <summary>File name truncated to 36 chars for display.</summary>
    public string ShortName => FileName.Length > 36 ? FileName[..36] + "…" : FileName;
}
