using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly AppPreferences _prefs;
    private readonly MobileLlmService _llmService;
    private readonly StoryLibraryService _storyService;
    private readonly ChatSessionService _chatSessions;
    private readonly WorkContextService _workContext;
    private readonly HistoryLibraryService _history;

#if ANDROID
    private Android.Speech.Tts.TextToSpeech? _nativeTts;
    private bool _nativeTtsReady;
#endif

    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _ttsEnabled;
    [ObservableProperty] private string _selectedModel = string.Empty;
    [ObservableProperty] private bool _showModelPicker;
    [ObservableProperty] private string _modelInfo = string.Empty;
    [ObservableProperty] private string _selectedVoice = "Chelsie";
    [ObservableProperty] private bool _showVoicePicker;
    [ObservableProperty] private string _ttsStatus = string.Empty;
    [ObservableProperty] private bool _isOfflineMode = true;
    [ObservableProperty] private string _modeLabel = "OFFLINE";
    [ObservableProperty] private string _modeColor = "#4A90D9";
    [ObservableProperty] private string _sessionTitle = "New Chat";
    [ObservableProperty] private string _sessionHint = "Save chats for later, or restart to begin a new thread.";
    [ObservableProperty] private StoryProfileItem? _selectedStory;
    [ObservableProperty] private string _storyContextSummary = "No active story selected.";

    [ObservableProperty] private bool _showImageGen;
    [ObservableProperty] private string _imagePrompt = string.Empty;
    [ObservableProperty] private bool _isGeneratingImage;
    [ObservableProperty] private string _generatedImageUrl = string.Empty;
    [ObservableProperty] private bool _hasGeneratedImage;
    [ObservableProperty] private string _imageGenStatus = string.Empty;

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();
    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailableVoices { get; } = new()
    {
        "Chelsie", "Ethan", "Aidan", "Luna", "Aria", "Sage", "Quinn", "Nova", "Willow"
    };
    public ObservableCollection<StoryProfileItem> Stories { get; } = new();
    public ObservableCollection<ChatSessionItem> SavedSessions { get; } = new();

    private string _currentSessionId = Guid.NewGuid().ToString("N");

    public ChatViewModel(
        AppPreferences prefs,
        MobileLlmService llmService,
        StoryLibraryService storyService,
        ChatSessionService chatSessions,
        WorkContextService workContext,
        HistoryLibraryService history)
    {
        _prefs = prefs;
        _llmService = llmService;
        _storyService = storyService;
        _chatSessions = chatSessions;
        _workContext = workContext;
        _history = history;

        TtsEnabled = _prefs.TtsEnabled;
        SelectedModel = _prefs.SelectedLlmModel;
        SelectedVoice = string.IsNullOrEmpty(_prefs.TtsVoice) ? "Chelsie" : _prefs.TtsVoice;
        IsOfflineMode = true;
        UpdateModeDisplay();
        ModelInfo = string.IsNullOrWhiteSpace(SelectedModel) ? "Local model" : $"Model: {SelectedModel}";
        InitializeNativeTts();

        Messages.Add(new ChatMessageModel
        {
            Role = "assistant",
            Content = "Hi! I'm MC Offline Chat, your offline-first creative assistant.\n\n" +
                      "Use this space for stories, journaling, planning, and helpful conversation.\n" +
                      "Save a chat when you want to come back to it later.",
            Timestamp = DateTime.Now
        });

        _ = LoadModelsAsync();
        _ = RefreshStoriesAsync();
        _ = LoadSavedSessionsAsync();
    }

    public async Task OnAppearingAsync()
    {
        await RefreshStoriesAsync();
        await LoadSavedSessionsAsync();
        await ApplyPendingOpenAsync();
    }

    partial void OnSelectedStoryChanged(StoryProfileItem? value)
    {
        UpdateStoryContext();
        _prefs.AiCreativeMode = value != null || _prefs.AiCreativeMode;
    }

    partial void OnSelectedModelChanged(string value)
    {
        _prefs.SelectedLlmModel = value ?? string.Empty;
        ModelInfo = string.IsNullOrWhiteSpace(value) ? "Local model" : $"Model: {value}";
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        _prefs.TtsVoice = value ?? string.Empty;
    }

    private void UpdateStoryContext()
    {
        StoryContextSummary = SelectedStory is null
            ? "No active story selected."
            : SelectedStory.BuildPromptBlock();

        SessionHint = SelectedStory is null
            ? "Save chats for later, or restart to begin a new thread."
            : $"Active story: {SelectedStory.DisplayTitle}. Story metadata will be injected into prompts.";
    }

    private async Task RefreshStoriesAsync()
    {
        Stories.Clear();
        foreach (var story in await _storyService.LoadAsync())
            Stories.Add(story);

        if (SelectedStory == null && Stories.Count > 0)
            SelectedStory = Stories[0];

        UpdateStoryContext();
    }

    private async Task LoadSavedSessionsAsync()
    {
        SavedSessions.Clear();
        foreach (var session in await _chatSessions.LoadAsync())
            SavedSessions.Add(session);
    }

    private void InitializeNativeTts()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            if (context == null) { _nativeTtsReady = false; return; }
            _nativeTts = new Android.Speech.Tts.TextToSpeech(context,
                new TtsInitListener(status =>
                {
                    if (status == Android.Speech.Tts.OperationResult.Success)
                    {
                        _nativeTtsReady = true;
                        _nativeTts?.SetLanguage(Java.Util.Locale.Us);
                        _nativeTts?.SetSpeechRate(0.95f);
                        _nativeTts?.SetPitch(1.0f);
                    }
                }));
        }
        catch (Exception ex)
        {
            _nativeTtsReady = false;
            SglLogger.Warning("[Chat] Native TTS initialisation failed: {0}", ex.Message);
        }
#endif
    }

    private Task LoadModelsAsync()
    {
        try
        {
            var models = _llmService.GetDownloadedModels();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AvailableModels.Clear();
                foreach (var local in models)
                    AvailableModels.Add(local);

                if (string.IsNullOrWhiteSpace(SelectedModel) && models.Count > 0)
                {
                    SelectedModel = models[0];
                    ModelInfo = $"Model: {SelectedModel}";
                }
            });
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[Chat] Model enumeration failed: {0}", ex.Message);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = MessageText?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Messages.Add(new ChatMessageModel
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.Now
        });

        MessageText = string.Empty;
        IsSending = true;

        try
        {
            if (string.IsNullOrWhiteSpace(_sessionTitle) || _sessionTitle == "New Chat")
                SessionTitle = text.Length > 40 ? text[..40].Trim() + "..." : text;

            if (text.StartsWith("/"))
            {
                await HandleCommandAsync(text);
                return;
            }

            var systemPrompt = BuildSystemPrompt();
            var response = await _llmService.RunLocalInferenceAsync(text, systemPrompt);

            var assistantMsg = new ChatMessageModel
            {
                Role = "assistant",
                Content = response,
                Timestamp = DateTime.Now
            };
            assistantMsg.ParseThinkingTags();
            Messages.Add(assistantMsg);

            if (TtsEnabled)
                await PlayTtsAsync(assistantMsg.DisplayText);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageModel
            {
                Role = "assistant",
                Content = $"Error: {ex.Message}",
                Timestamp = DateTime.Now
            });
            SglLogger.Warning("[Chat] Send failed: {0}", ex.Message);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task SaveChatAsync()
    {
        var session = new ChatSessionItem
        {
            Id = _currentSessionId,
            Title = string.IsNullOrWhiteSpace(SessionTitle) ? "Untitled Chat" : SessionTitle.Trim(),
            StoryId = SelectedStory?.Id ?? string.Empty,
            StoryTitle = SelectedStory?.DisplayTitle ?? string.Empty,
            Messages = Messages.Select(m => new ChatMessageModel
            {
                Role = m.Role,
                Content = m.Content,
                Timestamp = m.Timestamp,
                ThinkingContent = m.ThinkingContent,
                IsThinkingExpanded = m.IsThinkingExpanded
            }).ToList()
        };

        await _chatSessions.SaveAsync(session);
        await LoadSavedSessionsAsync();
        SessionHint = $"Saved {session.DisplayTitle}.";
    }

    [RelayCommand]
    private async Task RestartChatAsync()
    {
        await SaveChatAsync();
        Messages.Clear();
        _currentSessionId = Guid.NewGuid().ToString("N");
        SessionTitle = "New Chat";
        Messages.Add(new ChatMessageModel
        {
            Role = "assistant",
            Content = "Fresh chat started. Pick a story, or just begin typing.",
            Timestamp = DateTime.Now
        });
        UpdateStoryContext();
    }

    [RelayCommand]
    private async Task LoadSessionAsync(ChatSessionItem? session)
    {
        if (session == null) return;
        var loaded = await _chatSessions.GetByIdAsync(session.Id);
        if (loaded == null)
        {
            SessionHint = "Saved chat could not be found.";
            return;
        }

        Messages.Clear();
        foreach (var message in loaded.Messages)
            Messages.Add(message);

        _currentSessionId = loaded.Id;
        SessionTitle = loaded.DisplayTitle;

        if (!string.IsNullOrWhiteSpace(loaded.StoryId))
        {
            SelectedStory = Stories.FirstOrDefault(x => x.Id == loaded.StoryId);
        }

        SessionHint = $"Loaded {loaded.DisplayTitle}.";
    }

    private async Task ApplyPendingOpenAsync()
    {
        var request = _workContext.Consume();
        if (request is null || request.Value.Target != WorkOpenTarget.Chat)
            return;

        var session = await _chatSessions.GetByIdAsync(request.Value.Id);
        if (session == null)
        {
            SessionHint = "Requested chat session could not be found.";
            return;
        }

        await LoadSessionAsync(session);
    }

    private string BuildSystemPrompt()
    {
        var customPrompt = _prefs.AiSystemPrompt;
        var creative = _prefs.AiCreativeMode;

        const string coreDirective = """
            You are MC Offline Chat, an offline-first creative assistant for stories, journaling, brainstorming, and helpful conversation.

            IMPORTANT RULES:
            - Respond in the user's language naturally
            - Be conversational and helpful
            - Keep the current world rules intact if a story is active
            - Treat the last five chapters as canon when provided
            - Prefer warmth, imagination, and useful structure
            """;

        var prompt = coreDirective;
        if (creative)
            prompt += "\n\nCreative mode is enabled. Favor vivid scenes, stronger hooks, and expressive language.";

        if (SelectedStory != null)
            prompt += "\n\nActive story context:\n" + SelectedStory.BuildPromptBlock();

        if (!string.IsNullOrWhiteSpace(customPrompt))
            prompt += "\n\nUser custom prompt:\n" + customPrompt;

        return prompt;
    }

    private void UpdateModeDisplay()
    {
        ModeLabel = "OFFLINE";
        ModeColor = "#4A90D9";
    }

    private async Task PlayTtsAsync(string text)
    {
        if (!TtsEnabled || string.IsNullOrWhiteSpace(text)) return;
#if ANDROID
        if (_nativeTtsReady && _nativeTts != null)
        {
            try
            {
                _nativeTts.Stop();
                _nativeTts.Speak(text, Android.Speech.Tts.QueueMode.Flush, null, Guid.NewGuid().ToString("N")[..8]);
                return;
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[Chat] Native TTS speak failed: {0}", ex.Message);
            }
        }
#endif
        await _llmService.RunLocalInferenceAsync(text, BuildSystemPrompt());
    }

    [RelayCommand]
    private void ToggleOfflineMode()
    {
        IsOfflineMode = true;
        _prefs.UseOfflineMode = true;
        UpdateModeDisplay();
    }

    [RelayCommand]
    private void ToggleModelPicker() => ShowModelPicker = !ShowModelPicker;

    [RelayCommand]
    private void ToggleVoicePicker() => ShowVoicePicker = !ShowVoicePicker;

    [RelayCommand]
    private void ToggleTts()
    {
        TtsEnabled = !TtsEnabled;
        _prefs.TtsEnabled = TtsEnabled;
    }

    [RelayCommand]
    private void ClearChat() => Messages.Clear();

    private async Task HandleCommandAsync(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "/models":
                await LoadModelsAsync();
                Messages.Add(new ChatMessageModel { Role = "assistant", Content = $"Local models: {(AvailableModels.Count == 0 ? "none yet" : string.Join(", ", AvailableModels))}", Timestamp = DateTime.Now });
                break;
            case "/switch":
                Messages.Add(new ChatMessageModel { Role = "assistant", Content = "Open Settings to choose a local GGUF model.", Timestamp = DateTime.Now });
                break;
            case "/journal":
                Messages.Add(new ChatMessageModel { Role = "assistant", Content = "Tell me the mood, event, or reflection and I will help you shape it into a journal entry.", Timestamp = DateTime.Now });
                break;
            default:
                Messages.Add(new ChatMessageModel { Role = "assistant", Content = "Unknown command. Try /models, /switch, or /journal.", Timestamp = DateTime.Now });
                break;
        }
    }

#if ANDROID
    private sealed class TtsInitListener : Java.Lang.Object, Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        private readonly Action<Android.Speech.Tts.OperationResult> _callback;
        public TtsInitListener(Action<Android.Speech.Tts.OperationResult> callback) => _callback = callback;
        public void OnInit(Android.Speech.Tts.OperationResult status) => _callback(status);
    }
#endif
}
