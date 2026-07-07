namespace MCOfflineChat.Mobile.Models;

public sealed class LocalDocumentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public string Extension { get; set; } = ".txt";
    public string Language { get; set; } = "Text";
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => $"{Name}{Extension}";
    public string Subtitle => $"{Language} • {ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
