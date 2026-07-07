using System.Text.Json;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public sealed class HistoryLibraryService
{
    private readonly string _root;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public HistoryLibraryService(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(FileSystem.AppDataDirectory, "history");
        _indexPath = Path.Combine(_root, "index.json");
        Directory.CreateDirectory(_root);
    }

    public async Task<List<WorkHistoryItem>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return new List<WorkHistoryItem>();

            var json = await File.ReadAllTextAsync(_indexPath);
            return JsonSerializer.Deserialize<List<WorkHistoryItem>>(json, _jsonOptions)?
                .OrderByDescending(x => x.ModifiedUtc)
                .ToList() ?? new List<WorkHistoryItem>();
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[History] Load failed: {0}", ex.Message);
            return new List<WorkHistoryItem>();
        }
    }

    public async Task UpsertAsync(WorkHistoryItem item)
    {
        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Kind == item.Kind && x.EntityId == item.EntityId);
        items.Add(item);
        await SaveAsync(items.OrderByDescending(x => x.ModifiedUtc).ToList());
    }

    public async Task RemoveAsync(WorkKind kind, string entityId)
    {
        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Kind == kind && x.EntityId == entityId);
        await SaveAsync(items.OrderByDescending(x => x.ModifiedUtc).ToList());
    }

    public async Task<WorkHistoryItem?> FindAsync(WorkKind kind, string entityId)
        => (await LoadAsync()).FirstOrDefault(x => x.Kind == kind && x.EntityId == entityId);

    private async Task<List<WorkHistoryItem>> LoadInternalAsync()
    {
        if (!File.Exists(_indexPath))
            return new List<WorkHistoryItem>();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<List<WorkHistoryItem>>(json, _jsonOptions) ?? new List<WorkHistoryItem>();
    }

    private async Task SaveAsync(List<WorkHistoryItem> items)
    {
        Directory.CreateDirectory(_root);
        var json = JsonSerializer.Serialize(items, _jsonOptions);
        await File.WriteAllTextAsync(_indexPath, json);
    }
}
