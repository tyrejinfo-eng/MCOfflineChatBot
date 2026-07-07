namespace MCOfflineChat.Mobile.Models;

public sealed class StoryCharacterItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Age { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Clothes { get; set; } = string.Empty;
    public string ItemsText { get; set; } = string.Empty;
    public string MagicalAbilities { get; set; } = string.Empty;
    public string Likes { get; set; } = string.Empty;
    public string Dislikes { get; set; } = string.Empty;
    public string Fears { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string AvatarPath { get; set; } = string.Empty;
    public List<CharacterItemEvent> ItemLog { get; set; } = new();

    public string DisplayName
        => string.Join(" ", new[] { Name?.Trim(), Surname?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)))
           .Trim();

    public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? "Unnamed Character" : DisplayName;

    public IReadOnlyList<string> Items => SplitList(ItemsText, 50);

    public string Summary
        => string.IsNullOrWhiteSpace(Species)
            ? FriendlyName
            : $"{FriendlyName} • {Species.Trim()}";

    public void AppendLog(string action, string itemName, string notes)
    {
        ItemLog ??= new List<CharacterItemEvent>();
        ItemLog.Add(new CharacterItemEvent
        {
            Action = string.IsNullOrWhiteSpace(action) ? "updated" : action.Trim(),
            ItemName = string.IsNullOrWhiteSpace(itemName) ? "item" : itemName.Trim(),
            Notes = notes?.Trim() ?? string.Empty,
            TimestampUtc = DateTime.UtcNow
        });
    }

    private static List<string> SplitList(string? value, int maxItems)
        => string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value
                .Split(new[] { '\n', '\r', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
}

public sealed class CharacterItemEvent
{
    public string Action { get; set; } = "updated";
    public string ItemName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string Display => $"{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm} • {Action} • {ItemName}";
}
