namespace MCOfflineChat.Mobile.Models;

public sealed class ChatSessionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Chat";
    public string StoryId { get; set; } = string.Empty;
    public string StoryTitle { get; set; } = string.Empty;
    public List<ChatMessageModel> Messages { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled Chat" : Title.Trim();
    public string Subtitle => string.IsNullOrWhiteSpace(StoryTitle)
        ? $"{Messages.Count} message(s)"
        : $"{StoryTitle} • {Messages.Count} message(s)";
}
