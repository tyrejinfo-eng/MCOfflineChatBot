using CommunityToolkit.Mvvm.ComponentModel;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly AppPreferences _prefs;
    private readonly MobileLlmService _llmService;
    private readonly LocalDocumentService _documentService;
    private readonly StoryLibraryService _storyService;

    [ObservableProperty] private string _headline = "MC Offline Chat";
    [ObservableProperty] private string _offlineStatus = "Offline-first mode is enabled.";
    [ObservableProperty] private string _modelStatus = "No GGUF model loaded yet.";
    [ObservableProperty] private int _documentCount;
    [ObservableProperty] private int _storyCount;
    [ObservableProperty] private string _assistantName = "MC Offline Chat";
    [ObservableProperty] private bool _ttsEnabled;
    [ObservableProperty] private string _lastSavedPrompt = string.Empty;
    [ObservableProperty] private string _runtimeHint = "Local model, docs, stories, Git search, telemetry.";
    [ObservableProperty] private string _tabsSummary = string.Empty;
    [ObservableProperty] private string _lastStoryImagePath = string.Empty;
    [ObservableProperty] private string _lastStoryTitle = string.Empty;
    [ObservableProperty] private string _lastCharacterName = string.Empty;

    public DashboardViewModel(
        AppPreferences prefs,
        MobileLlmService llmService,
        LocalDocumentService documentService,
        StoryLibraryService storyService)
    {
        _prefs = prefs;
        _llmService = llmService;
        _documentService = documentService;
        _storyService = storyService;
        Refresh();
    }

    public void OnAppearing() => Refresh();

    private void Refresh()
    {
        AssistantName = _prefs.AiAssistantName;
        TtsEnabled = _prefs.TtsEnabled;
        LastSavedPrompt = string.IsNullOrWhiteSpace(_prefs.AiSystemPrompt)
            ? "No custom prompt saved."
            : _prefs.AiSystemPrompt;

        DocumentCount = _documentService.Count();
        StoryCount = _storyService.Count();
        TabsSummary = string.Join(" • ", NavigationManifest.Tabs.Select(t => t.Title));

        var latestStory = _storyService.GetLatest();
        if (latestStory != null)
        {
            LastStoryImagePath = latestStory.ImagePath;
            LastStoryTitle = latestStory.DisplayTitle;
            LastCharacterName = latestStory.Characters.FirstOrDefault()?.FriendlyName ?? string.Empty;
        }
        else
        {
            LastStoryImagePath = string.Empty;
            LastStoryTitle = string.Empty;
            LastCharacterName = string.Empty;
        }

        ModelStatus = _llmService.IsModelDownloaded
            ? $"Loaded model: {_llmService.CurrentModelName}"
            : "No GGUF model loaded. Import one in Settings.";

        OfflineStatus = _llmService.IsLocalInferenceAvailable
            ? "Local inference ready."
            : "Offline-first mode ready.";
    }
}
