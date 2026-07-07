using System.Text.Json;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public sealed class ChatSessionService
{
    private readonly string _root;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly HistoryLibraryService? _history;

    public ChatSessionService(string? rootDirectory = null, HistoryLibraryService? history = null)
    {
        _root = rootDirectory ?? Path.Combine(FileSystem.AppDataDirectory, "chat_sessions");
        _indexPath = Path.Combine(_root, "index.json");
        _history = history;
        Directory.CreateDirectory(_root);
    }

    public async Task<List<ChatSessionItem>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return new List<ChatSessionItem>();

            var json = await File.ReadAllTextAsync(_indexPath);
            return JsonSerializer.Deserialize<List<ChatSessionItem>>(json, _jsonOptions)?
                .OrderByDescending(x => x.ModifiedUtc)
                .ToList() ?? new List<ChatSessionItem>();
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[ChatSessions] Load failed: {0}", ex.Message);
            return new List<ChatSessionItem>();
        }
    }

    public async Task<ChatSessionItem?> GetByIdAsync(string id)
        => (await LoadAsync()).FirstOrDefault(x => x.Id == id);

    public async Task<ChatSessionItem> SaveAsync(ChatSessionItem session)
    {
        session.ModifiedUtc = DateTime.UtcNow;
        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Id == session.Id);
        items.Add(session);
        await SaveIndexAsync(items.OrderByDescending(x => x.ModifiedUtc).ToList());

        if (_history != null)
        {
            await _history.UpsertAsync(new WorkHistoryItem
            {
                Kind = WorkKind.Chat,
                EntityId = session.Id,
                Title = session.DisplayTitle,
                Subtitle = session.Subtitle,
                Preview = session.Messages.LastOrDefault()?.DisplayText ?? string.Empty,
                ModifiedUtc = session.ModifiedUtc
            });
        }

        return session;
    }

    public async Task DeleteAsync(ChatSessionItem session)
    {
        var items = await LoadInternalAsync();
        items.RemoveAll(x => x.Id == session.Id);
        await SaveIndexAsync(items);
        if (_history != null)
            await _history.RemoveAsync(WorkKind.Chat, session.Id);
    }

    private async Task<List<ChatSessionItem>> LoadInternalAsync()
    {
        if (!File.Exists(_indexPath))
            return new List<ChatSessionItem>();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<List<ChatSessionItem>>(json, _jsonOptions) ?? new List<ChatSessionItem>();
    }

    private async Task SaveIndexAsync(List<ChatSessionItem> items)
    {
        Directory.CreateDirectory(_root);
        var json = JsonSerializer.Serialize(items, _jsonOptions);
        await File.WriteAllTextAsync(_indexPath, json);
    }
}
