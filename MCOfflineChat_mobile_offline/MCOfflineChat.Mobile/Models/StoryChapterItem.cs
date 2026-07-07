namespace MCOfflineChat.Mobile.Models;

public sealed class StoryChapterItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Chapter";
    public string Content { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Chapter" : Title.Trim();
    public string Preview
        => string.IsNullOrWhiteSpace(Content)
            ? "No chapter text yet."
            : Content.Trim().Length > 110 ? Content.Trim()[..110] + "..." : Content.Trim();
}
