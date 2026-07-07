namespace MCOfflineChat.Mobile.Models;

public sealed class StoryProfileItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Story";
    public string WorldName { get; set; } = string.Empty;
    public string WorldType { get; set; } = string.Empty;
    public string WorldTime { get; set; } = string.Empty;
    public string WorldInhabitants { get; set; } = string.Empty;
    public string TerrainType { get; set; } = string.Empty;
    public string TagsText { get; set; } = string.Empty;
    public string MagicSystem { get; set; } = string.Empty;
    public string HardWorldRules { get; set; } = string.Empty;
    public string Prologue { get; set; } = string.Empty;
    public string StorySummary { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public List<StoryCharacterItem> Characters { get; set; } = new();
    public List<StoryChapterItem> PreviousChapters { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled Story" : Title.Trim();

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(WorldName)) parts.Add(WorldName.Trim());
            if (!string.IsNullOrWhiteSpace(WorldType)) parts.Add(WorldType.Trim());
            if (Characters.Count > 0) parts.Add($"{Characters.Count} character(s)");
            return parts.Count == 0 ? "Tap to define a world or character" : string.Join(" • ", parts);
        }
    }

    public IReadOnlyList<string> Tags => SplitList(TagsText, 50);

    public StoryCharacterItem GetOrCreateFirstCharacter()
    {
        if (Characters.Count == 0)
        {
            Characters.Add(new StoryCharacterItem());
        }

        return Characters[0];
    }

    public void Normalize()
    {
        Characters ??= new List<StoryCharacterItem>();
        PreviousChapters ??= new List<StoryChapterItem>();

        if (Characters.Count > 10)
            Characters = Characters.Take(10).ToList();

        if (PreviousChapters.Count > 5)
            PreviousChapters = PreviousChapters.OrderByDescending(x => x.ModifiedUtc).Take(5).OrderBy(x => x.CreatedUtc).ToList();

        TagsText = string.Join(", ", Tags);
    }

    public string BuildPromptBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Story title: {DisplayTitle}");
        if (!string.IsNullOrWhiteSpace(WorldName)) sb.AppendLine($"World: {WorldName.Trim()}");
        if (!string.IsNullOrWhiteSpace(WorldType)) sb.AppendLine($"World type: {WorldType.Trim()}");
        if (!string.IsNullOrWhiteSpace(WorldTime)) sb.AppendLine($"World time: {WorldTime.Trim()}");
        if (!string.IsNullOrWhiteSpace(WorldInhabitants)) sb.AppendLine($"Inhabitants: {WorldInhabitants.Trim()}");
        if (!string.IsNullOrWhiteSpace(TerrainType)) sb.AppendLine($"Terrain: {TerrainType.Trim()}");
        if (Tags.Count > 0) sb.AppendLine($"Tags: {string.Join(", ", Tags)}");
        if (!string.IsNullOrWhiteSpace(MagicSystem)) sb.AppendLine($"Magic system: {MagicSystem.Trim()}");
        if (!string.IsNullOrWhiteSpace(HardWorldRules)) sb.AppendLine($"Hard rules: {HardWorldRules.Trim()}");
        if (!string.IsNullOrWhiteSpace(Prologue)) sb.AppendLine($"Prologue: {Prologue.Trim()}");
        if (Characters.Count > 0)
        {
            sb.AppendLine("Characters:");
            foreach (var character in Characters.Take(10))
            {
                sb.AppendLine($"- {character.FriendlyName}: {character.Description}");
            }
        }

        if (PreviousChapters.Count > 0)
        {
            sb.AppendLine("Previous chapters (max 5):");
            foreach (var chapter in PreviousChapters.TakeLast(5))
            {
                sb.AppendLine($"- {chapter.DisplayTitle}: {chapter.Preview}");
            }
        }

        return sb.ToString().Trim();
    }

    private static IReadOnlyList<string> SplitList(string? value, int maxItems)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Split(new[] { '\n', '\r', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
}
