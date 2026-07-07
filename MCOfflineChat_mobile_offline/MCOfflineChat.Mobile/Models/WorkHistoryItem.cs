namespace MCOfflineChat.Mobile.Models;

public enum WorkKind
{
    Chat,
    Document,
    Story
}

public sealed class WorkHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WorkKind Kind { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string Title { get; set; } = "Untitled";
    public string Subtitle { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public string OpenTarget => Kind switch
    {
        WorkKind.Document => "Documents",
        WorkKind.Story => "Stories",
        _ => "Chat"
    };
}
