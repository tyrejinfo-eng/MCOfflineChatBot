using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Shared.Localization;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppPreferences _prefs;
    private readonly MobileLlmService _llmService;

    public static string VersionString => MCOfflineChat.Shared.VersionInfo.MobileVersion;

    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _ttsEnabled;
    [ObservableProperty] private bool _useOfflineMode;
    [ObservableProperty] private string _assistantName = string.Empty;
    [ObservableProperty] private string _systemPrompt = string.Empty;
    [ObservableProperty] private bool _creativeMode;
    [ObservableProperty] private string _selectedLanguage = string.Empty;
    [ObservableProperty] private string _selectedLlmModel = string.Empty;
    [ObservableProperty] private string _statusMessage = "Offline-only model manager.";
    [ObservableProperty] private string _versionDisplay = VersionString;
    [ObservableProperty] private string _modelDirectory = string.Empty;

    public ObservableCollection<string> DownloadedModels { get; } = new();
    public List<string> SupportedLanguages { get; } = LocalizationService.SupportedLanguages
        .Select(l => l.ToString()).ToList();

    public SettingsViewModel(AppPreferences prefs, MobileLlmService llmService)
    {
        _prefs = prefs;
        _llmService = llmService;

        NotificationsEnabled = _prefs.NotificationsEnabled;
        TtsEnabled = _prefs.TtsEnabled;
        UseOfflineMode = _prefs.UseOfflineMode;
        AssistantName = _prefs.AiAssistantName;
        SystemPrompt = _prefs.AiSystemPrompt;
        CreativeMode = _prefs.AiCreativeMode;
        SelectedLlmModel = _prefs.SelectedLlmModel;
        ModelDirectory = _llmService.ModelDirectory;

        SelectedLanguage = LocalizationService.SupportedLanguages
            .FirstOrDefault(l => l.Code == LocalizationService.Instance.CurrentLanguage)?.ToString()
            ?? LocalizationService.SupportedLanguages[0].ToString();

        RefreshModels();
    }

    public async Task OnAppearingAsync()
    {
        await Task.CompletedTask;
        RefreshModels();
    }

    partial void OnNotificationsEnabledChanged(bool value) => _prefs.NotificationsEnabled = value;
    partial void OnTtsEnabledChanged(bool value) => _prefs.TtsEnabled = value;
    partial void OnUseOfflineModeChanged(bool value) => _prefs.UseOfflineMode = value;
    partial void OnAssistantNameChanged(string value) => _prefs.AiAssistantName = value ?? string.Empty;
    partial void OnSystemPromptChanged(string value) => _prefs.AiSystemPrompt = value ?? string.Empty;
    partial void OnCreativeModeChanged(bool value) => _prefs.AiCreativeMode = value;

    partial void OnSelectedLanguageChanged(string value)
    {
        var langInfo = LocalizationService.SupportedLanguages.FirstOrDefault(l => l.ToString() == value);
        if (langInfo != null)
            LocalizationService.Instance.SetLanguage(langInfo.Code);
    }

    partial void OnSelectedLlmModelChanged(string value)
    {
        _prefs.SelectedLlmModel = value ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value))
            _llmService.SwitchModel(value);
    }

    [RelayCommand]
    private void RefreshModels()
    {
        DownloadedModels.Clear();
        foreach (var model in _llmService.GetDownloadedModels())
            DownloadedModels.Add(model);

        if (string.IsNullOrWhiteSpace(SelectedLlmModel) && DownloadedModels.Count > 0)
            SelectedLlmModel = DownloadedModels[0];

        StatusMessage = DownloadedModels.Count == 0
            ? "Import a GGUF model to begin."
            : $"Found {DownloadedModels.Count} local model(s) in {ModelDirectory}.";
    }

    [RelayCommand]
    private async Task ImportModelAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import a GGUF model"
            });

            if (file == null)
                return;

            var imported = await _llmService.ImportModelFromFileAsync(file);
            RefreshModels();
            if (imported && !string.IsNullOrWhiteSpace(_llmService.CurrentModelName))
                SelectedLlmModel = _llmService.CurrentModelName;

            StatusMessage = imported
                ? $"Imported {file.FileName}."
                : "Model import failed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            SglLogger.Warning("[Settings] Model import failed: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void ResetOfflineDefaults()
    {
        UseOfflineMode = true;
        NotificationsEnabled = true;
        TtsEnabled = false;
        CreativeMode = false;
        SelectedLanguage = SupportedLanguages.FirstOrDefault() ?? string.Empty;
        StatusMessage = "Offline defaults restored.";
    }
}
