using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryLibraryService _history;
    private readonly WorkContextService _workContext;

    [ObservableProperty] private string _statusText = "Recent work history from the device.";
    [ObservableProperty] private WorkHistoryItem? _selectedItem;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<WorkHistoryItem> Items { get; } = new();

    public HistoryViewModel(HistoryLibraryService history, WorkContextService workContext)
    {
        _history = history;
        _workContext = workContext;
    }

    public async Task OnAppearingAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            foreach (var item in await _history.LoadAsync())
                Items.Add(item);

            StatusText = Items.Count == 0
                ? "No saved work yet."
                : $"Loaded {Items.Count} recent item(s).";
            SelectedItem ??= Items.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to load history: {ex.Message}";
            SglLogger.Warning("[History] Load failed: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsChatAsync(WorkHistoryItem? item)
    {
        if (item == null || item.Kind != WorkKind.Chat) return;
        _workContext.RequestOpen(WorkOpenTarget.Chat, item.EntityId);
        await Shell.Current.GoToAsync("//ChatPage");
    }

    [RelayCommand]
    private async Task OpenAsDocumentAsync(WorkHistoryItem? item)
    {
        if (item == null || item.Kind != WorkKind.Document) return;
        _workContext.RequestOpen(WorkOpenTarget.Documents, item.EntityId);
        await Shell.Current.GoToAsync("//DocumentsPage");
    }

    [RelayCommand]
    private async Task OpenAsStoryAsync(WorkHistoryItem? item)
    {
        if (item == null || item.Kind != WorkKind.Story) return;
        _workContext.RequestOpen(WorkOpenTarget.Stories, item.EntityId);
        await Shell.Current.GoToAsync("//StoriesPage");
    }
}
