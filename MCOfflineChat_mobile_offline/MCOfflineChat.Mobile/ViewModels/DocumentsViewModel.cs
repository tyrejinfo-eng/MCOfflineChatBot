using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class DocumentsViewModel : ObservableObject
{
    private readonly LocalDocumentService _service;
    private readonly WorkContextService _workContext;
    private readonly HistoryLibraryService _history;

    public ObservableCollection<LocalDocumentItem> Documents { get; } = new();
    public ObservableCollection<DocumentTemplateOption> Templates { get; } = new();

    [ObservableProperty] private LocalDocumentItem? _selectedDocument;
    [ObservableProperty] private DocumentTemplateOption? _selectedTemplate;
    [ObservableProperty] private string _newDocumentName = "Untitled";
    [ObservableProperty] private string _editorContent = string.Empty;
    [ObservableProperty] private string _statusText = "Create or open a local document.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _languageHint = "C#, C++, JSON, Android XML, TypeScript, and JavaScript are supported.";

    public DocumentsViewModel(LocalDocumentService service, WorkContextService workContext, HistoryLibraryService history)
    {
        _service = service;
        _workContext = workContext;
        _history = history;

        foreach (var template in _service.GetTemplates())
            Templates.Add(template);

        SelectedTemplate = Templates.FirstOrDefault();
    }

    public async Task OnAppearingAsync()
    {
        await LoadAsync();
        await ApplyPendingOpenAsync();
    }

    partial void OnSelectedDocumentChanged(LocalDocumentItem? value)
    {
        if (value == null)
        {
            EditorContent = string.Empty;
            return;
        }

        NewDocumentName = value.Name;
        EditorContent = value.Content;
        SelectedTemplate = Templates.FirstOrDefault(t => t.Extension.Equals(value.Extension, StringComparison.OrdinalIgnoreCase))
            ?? SelectedTemplate;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Documents.Clear();
            foreach (var item in await _service.LoadAsync())
                Documents.Add(item);

            StatusText = Documents.Count == 0
                ? "No saved documents yet."
                : $"Loaded {Documents.Count} document(s).";

            SelectedDocument ??= Documents.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to load documents: {ex.Message}";
            SglLogger.Warning("[Documents] {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewDocumentAsync()
    {
        if (SelectedTemplate == null)
            SelectedTemplate = Templates.FirstOrDefault();

        if (SelectedTemplate == null)
        {
            StatusText = "No document templates available.";
            return;
        }

        var item = await _service.CreateAsync(NewDocumentName, SelectedTemplate);
        Documents.Insert(0, item);
        SelectedDocument = item;
        StatusText = $"Created {item.DisplayName}.";
        await _history.UpsertAsync(new WorkHistoryItem
        {
            Kind = WorkKind.Document,
            EntityId = item.Id,
            Title = item.DisplayName,
            Subtitle = item.Subtitle,
            Preview = item.Content,
            ModifiedUtc = item.ModifiedUtc
        });
    }

    [RelayCommand]
    private async Task SaveDocumentAsync()
    {
        if (SelectedDocument == null)
        {
            await NewDocumentAsync();
            if (SelectedDocument == null) return;
        }

        SelectedDocument.Name = string.IsNullOrWhiteSpace(NewDocumentName)
            ? SelectedDocument.Name
            : NewDocumentName.Trim();

        SelectedDocument.Content = EditorContent ?? string.Empty;

        if (SelectedTemplate != null)
        {
            SelectedDocument.Extension = SelectedTemplate.Extension;
            SelectedDocument.Language = SelectedTemplate.Language;
        }

        var savedId = SelectedDocument.Id;
        await _service.SaveAsync(SelectedDocument);
        await LoadAsync();
        SelectedDocument = Documents.FirstOrDefault(x => x.Id == savedId);
        StatusText = $"Saved {SelectedDocument?.DisplayName ?? SelectedDocument?.Name}.";
    }

    [RelayCommand]
    private async Task ImportDocumentAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open a document"
            });

            if (file == null)
                return;

            var imported = await _service.ImportAsync(file);
            Documents.Insert(0, imported);
            SelectedDocument = imported;
            StatusText = $"Imported {imported.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            SglLogger.Warning("[Documents] Import failed: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync()
    {
        if (SelectedDocument == null)
            return;

        var target = SelectedDocument;
        await _service.DeleteAsync(target);
        Documents.Remove(target);
        SelectedDocument = Documents.FirstOrDefault();
        StatusText = $"Deleted {target.DisplayName}.";
    }

    private async Task ApplyPendingOpenAsync()
    {
        var request = _workContext.Consume();
        if (request is not { } open || open.Target != WorkOpenTarget.Documents)
            return;

        var document = await _service.GetByIdAsync(open.Id);
        if (document == null)
        {
            StatusText = "Requested document could not be found.";
            return;
        }

        SelectedDocument = document;
        StatusText = $"Opened {document.DisplayName} from history.";
    }
}
