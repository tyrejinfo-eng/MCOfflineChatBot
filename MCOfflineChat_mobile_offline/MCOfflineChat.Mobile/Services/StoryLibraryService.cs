using System.Text.Json;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public sealed class StoryLibraryService
{
    private readonly string _root;
    private readonly string _imagesDir;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly HistoryLibraryService? _history;

    public string RootDirectory => _root;

    public StoryLibraryService(string? rootDirectory = null, HistoryLibraryService? history = null)
    {
        _root = rootDirectory ?? Path.Combine(FileSystem.AppDataDirectory, "stories");
        _imagesDir = Path.Combine(_root, "images");
        _indexPath = Path.Combine(_root, "index.json");
        _history = history;
        Directory.CreateDirectory(_imagesDir);
    }

    public async Task<List<StoryProfileItem>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return new List<StoryProfileItem>();

            var json = await File.ReadAllTextAsync(_indexPath);
            var stories = JsonSerializer.Deserialize<List<StoryProfileItem>>(json, _jsonOptions)?
                .OrderByDescending(x => x.ModifiedUtc)
                .ToList() ?? new List<StoryProfileItem>();

            foreach (var story in stories)
                story.Normalize();

            return stories;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Stories] Load failed: {0}", ex.Message);
            return new List<StoryProfileItem>();
        }
    }

    public async Task<StoryProfileItem?> GetByIdAsync(string id)
        => (await LoadAsync()).FirstOrDefault(x => x.Id == id);


    public async Task<StoryProfileItem?> GetLatestAsync()
        => (await LoadAsync()).FirstOrDefault();

    public StoryProfileItem? GetLatest()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return null;

            var json = File.ReadAllText(_indexPath);
            var stories = JsonSerializer.Deserialize<List<StoryProfileItem>>(json, _jsonOptions);
            var latest = stories?.OrderByDescending(x => x.ModifiedUtc).FirstOrDefault();
            latest?.Normalize();
            return latest;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Stories] GetLatest failed: {0}", ex.Message);
            return null;
        }
    }
    public async Task<StoryProfileItem> CreateAsync()
    {
        var item = new StoryProfileItem
        {
            Title = "Untitled Story",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        await SaveAsync(item);
        return item;
    }

    public async Task SaveAsync(StoryProfileItem item)
    {
        item.Normalize();
        item.ModifiedUtc = DateTime.UtcNow;

        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Id == item.Id);
        items.Add(item);
        await SaveIndexAsync(items.OrderByDescending(x => x.ModifiedUtc).ToList());

        if (_history != null)
        {
            await _history.UpsertAsync(new WorkHistoryItem
            {
                Kind = WorkKind.Story,
                EntityId = item.Id,
                Title = item.DisplayTitle,
                Subtitle = item.Subtitle,
                Preview = item.Prologue?.Trim().Length > 120 ? item.Prologue.Trim()[..120] + "..." : item.Prologue?.Trim() ?? string.Empty,
                ImagePath = item.ImagePath,
                ModifiedUtc = item.ModifiedUtc
            });
        }
    }

    public async Task DeleteAsync(StoryProfileItem item)
    {
        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Id == item.Id);
        await SaveIndexAsync(items.OrderByDescending(x => x.ModifiedUtc).ToList());

        if (!string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try
            {
                File.Delete(item.ImagePath);
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[Stories] Failed to delete image {0}: {1}", item.ImagePath, ex.Message);
            }
        }

        if (_history != null)
            _ = _history.RemoveAsync(WorkKind.Story, item.Id);
    }

    public async Task<string?> SetImageAsync(StoryProfileItem item, FileResult image)
    {
        await using var input = await image.OpenReadAsync();
        return await SetImageAsync(item, input, image.FileName);
    }

    public async Task<string?> SetImageAsync(StoryProfileItem item, Stream imageStream, string fileName)
    {
        Directory.CreateDirectory(_imagesDir);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".jpg";

        var safeFileName = $"{item.Id}_{SanitizeFileName(item.Title)}{ext}";
        var destPath = Path.Combine(_imagesDir, safeFileName);

        await using var output = File.Create(destPath);
        await imageStream.CopyToAsync(output);

        item.ImagePath = destPath;
        item.ModifiedUtc = DateTime.UtcNow;
        await SaveAsync(item);
        return destPath;
    }

    public async Task<string?> SetCharacterAvatarAsync(StoryProfileItem item, StoryCharacterItem character, Stream imageStream, string fileName)
    {
        Directory.CreateDirectory(_imagesDir);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".jpg";

        var safeFileName = $"{item.Id}_{character.Id}_{SanitizeFileName(character.FriendlyName)}{ext}";
        var destPath = Path.Combine(_imagesDir, safeFileName);

        await using var output = File.Create(destPath);
        await imageStream.CopyToAsync(output);

        character.AvatarPath = destPath;
        item.ModifiedUtc = DateTime.UtcNow;
        await SaveAsync(item);
        return destPath;
    }

    public int Count()
    {
        if (!File.Exists(_indexPath))
            return 0;

        try
        {
            var items = JsonSerializer.Deserialize<List<StoryProfileItem>>(File.ReadAllText(_indexPath), _jsonOptions);
            return items?.Count ?? 0;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Stories] Count failed: {0}", ex.Message);
            return 0;
        }
    }

    private async Task<List<StoryProfileItem>> LoadInternalAsync()
    {
        if (!File.Exists(_indexPath))
            return new List<StoryProfileItem>();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<List<StoryProfileItem>>(json, _jsonOptions) ?? new List<StoryProfileItem>();
    }

    private async Task SaveIndexAsync(List<StoryProfileItem> items)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_imagesDir);
        var json = JsonSerializer.Serialize(items, _jsonOptions);
        await File.WriteAllTextAsync(_indexPath, json);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Story" : safe;
    }
}
