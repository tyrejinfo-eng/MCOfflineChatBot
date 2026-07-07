using System.Text.Json;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public sealed class LocalDocumentService
{
    private readonly string _root;
    private readonly string _documentsDir;
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly HistoryLibraryService? _history;

    public string RootDirectory => _root;

    public LocalDocumentService(string? rootDirectory = null, HistoryLibraryService? history = null)
    {
        _root = rootDirectory ?? Path.Combine(FileSystem.AppDataDirectory, "documents");
        _documentsDir = Path.Combine(_root, "files");
        _indexPath = Path.Combine(_root, "index.json");
        _history = history;
        Directory.CreateDirectory(_documentsDir);
    }

    public async Task<List<LocalDocumentItem>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return new List<LocalDocumentItem>();

            var json = await File.ReadAllTextAsync(_indexPath);
            var items = JsonSerializer.Deserialize<List<LocalDocumentItem>>(json, _jsonOptions) ?? new List<LocalDocumentItem>();

            foreach (var item in items)
            {
                if (File.Exists(item.FilePath))
                    item.Content = await File.ReadAllTextAsync(item.FilePath);
            }

            return items.OrderByDescending(x => x.ModifiedUtc).ToList();
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Documents] Failed to load document index: {0}", ex.Message);
            return new List<LocalDocumentItem>();
        }
    }

    public async Task<LocalDocumentItem?> GetByIdAsync(string id)
        => (await LoadAsync()).FirstOrDefault(x => x.Id == id);

    public async Task<LocalDocumentItem> CreateAsync(string name, DocumentTemplateOption template)
    {
        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? template.Name : name);
        var item = new LocalDocumentItem
        {
            Name = safeName,
            Extension = template.Extension,
            Language = template.Language,
            Content = template.StarterText,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };

        item.FilePath = BuildPath(item);
        await SaveDocumentFileAsync(item);
        await UpsertAsync(item);
        await PublishHistoryAsync(item);
        return item;
    }

    public async Task<LocalDocumentItem> ImportAsync(FileResult file)
    {
        await using var input = await file.OpenReadAsync();
        return await ImportAsync(input, file.FileName);
    }

    public async Task<LocalDocumentItem> ImportAsync(Stream contentStream, string fileName)
    {
        using var reader = new StreamReader(contentStream, leaveOpen: true);
        var content = await reader.ReadToEndAsync();

        var ext = Path.GetExtension(fileName);
        var template = GetTemplateForExtension(ext);
        var item = new LocalDocumentItem
        {
            Name = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName)),
            Extension = string.IsNullOrWhiteSpace(ext) ? template.Extension : ext,
            Language = template.Language,
            Content = content,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };

        item.FilePath = BuildPath(item);
        await SaveDocumentFileAsync(item);
        await UpsertAsync(item);
        await PublishHistoryAsync(item);
        return item;
    }

    public async Task SaveAsync(LocalDocumentItem item)
    {
        item.ModifiedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(item.FilePath))
            item.FilePath = BuildPath(item);

        await SaveDocumentFileAsync(item);
        await UpsertAsync(item);
        await PublishHistoryAsync(item);
    }

    public async Task DeleteAsync(LocalDocumentItem item)
    {
        try
        {
            if (File.Exists(item.FilePath))
                File.Delete(item.FilePath);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Documents] Failed to delete file {0}: {1}", item.FilePath, ex.Message);
        }

        var all = await LoadIndexInternalAsync();
        all.RemoveAll(x => x.Id == item.Id);
        await SaveIndexAsync(all);
        if (_history != null)
            _ = _history.RemoveAsync(WorkKind.Document, item.Id);
    }

    public int Count()
    {
        if (!File.Exists(_indexPath))
            return 0;

        try
        {
            var items = JsonSerializer.Deserialize<List<LocalDocumentItem>>(File.ReadAllText(_indexPath), _jsonOptions);
            return items?.Count ?? 0;
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Documents] Count failed: {0}", ex.Message);
            return 0;
        }
    }

    public IEnumerable<DocumentTemplateOption> GetTemplates() => SeedTemplates();

    public static DocumentTemplateOption GetTemplateForExtension(string extension)
    {
        var ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
        return SeedTemplates().FirstOrDefault(x => x.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
            ?? SeedTemplates().Last();
    }

    private async Task UpsertAsync(LocalDocumentItem item)
    {
        var all = await LoadIndexInternalAsync();
        all.RemoveAll(x => x.Id == item.Id);
        all.Add(item);
        await SaveIndexAsync(all.OrderByDescending(x => x.ModifiedUtc).ToList());
    }

    private async Task<List<LocalDocumentItem>> LoadIndexInternalAsync()
    {
        if (!File.Exists(_indexPath))
            return new List<LocalDocumentItem>();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<List<LocalDocumentItem>>(json, _jsonOptions) ?? new List<LocalDocumentItem>();
    }

    private async Task SaveIndexAsync(List<LocalDocumentItem> items)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_documentsDir);
        var json = JsonSerializer.Serialize(items, _jsonOptions);
        await File.WriteAllTextAsync(_indexPath, json);
    }

    private async Task SaveDocumentFileAsync(LocalDocumentItem item)
    {
        Directory.CreateDirectory(_documentsDir);
        await File.WriteAllTextAsync(item.FilePath, item.Content ?? string.Empty);
    }

    private async Task PublishHistoryAsync(LocalDocumentItem item)
    {
        if (_history == null) return;

        await _history.UpsertAsync(new WorkHistoryItem
        {
            Kind = WorkKind.Document,
            EntityId = item.Id,
            Title = item.DisplayName,
            Subtitle = item.Subtitle,
            Preview = item.Content?.Trim().Length > 120 ? item.Content.Trim()[..120] + "..." : item.Content?.Trim() ?? string.Empty,
            ModifiedUtc = item.ModifiedUtc
        });
    }

    private string BuildPath(LocalDocumentItem item)
    {
        var fileName = $"{item.Id}_{SanitizeFileName(item.Name)}{item.Extension}";
        return Path.Combine(_documentsDir, fileName);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Untitled" : safe;
    }

    private static List<LocalDocumentItem> SeedDocuments()
    {
        var templates = SeedTemplates();
        var seed = new List<LocalDocumentItem>();

        foreach (var template in templates.Take(3))
        {
            var item = new LocalDocumentItem
            {
                Name = template.Name,
                Extension = template.Extension,
                Language = template.Language,
                Content = template.StarterText,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };
            item.FilePath = Path.Combine(FileSystem.AppDataDirectory, "documents", "files", $"{item.Id}_{item.Name}{item.Extension}");
            seed.Add(item);
        }

        return seed;
    }

    private static List<DocumentTemplateOption> SeedTemplates() => new()
    {
        new DocumentTemplateOption
        {
            Name = "C# Sample",
            Extension = ".cs",
            Language = "C#",
            StarterText = """
using System;

namespace MCOfflineChat.Offline;

public static class Sample
{
    public static string Speak() => "MC Offline Chat";
}
"""
        },
        new DocumentTemplateOption
        {
            Name = "C++ Sample",
            Extension = ".cpp",
            Language = "C++",
            StarterText = """
#include <iostream>

int main()
{
    std::cout << "MC Offline Chat" << std::endl;
    return 0;
}
"""
        },
        new DocumentTemplateOption
        {
            Name = "JSON Draft",
            Extension = ".json",
            Language = "JSON",
            StarterText = """
{
  "title": "MC Offline Chat",
  "offline": true,
  "notes": []
}
"""
        },
        new DocumentTemplateOption
        {
            Name = "TypeScript Draft",
            Extension = ".ts",
            Language = "TypeScript",
            StarterText = """
export function speak(message: string): string {
  return `MC Offline Chat: ${message}`;
}
"""
        },
        new DocumentTemplateOption
        {
            Name = "JavaScript Draft",
            Extension = ".js",
            Language = "JavaScript",
            StarterText = """
function speak(message) {
  return `MC Offline Chat: ${message}`;
}
"""
        },
        new DocumentTemplateOption
        {
            Name = "Android XML",
            Extension = ".xml",
            Language = "Android",
            StarterText = """
<?xml version="1.0" encoding="utf-8"?>
<manifest package="com.monstercoder.mcofflinechat">
</manifest>
"""
        }
    };
}
