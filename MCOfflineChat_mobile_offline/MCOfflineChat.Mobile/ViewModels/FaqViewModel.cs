using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class FaqViewModel : ObservableObject
{
    [ObservableProperty] private string _questionText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Browse the offline FAQ or ask a new question.";

    public ObservableCollection<FaqItem> FaqItems { get; } = new();

    public FaqViewModel()
    {
        LoadSeedItems();
    }

    public void OnAppearing()
    {
        if (FaqItems.Count == 0)
            LoadSeedItems();
    }


    [RelayCommand]
    private Task LoadFaqAsync()
    {
        if (FaqItems.Count == 0)
            LoadSeedItems();

        StatusMessage = $"Loaded {FaqItems.Count} offline FAQ item(s).";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task AskQuestionAsync()
    {
        if (string.IsNullOrWhiteSpace(QuestionText))
        {
            StatusMessage = "Please enter a question.";
            return Task.CompletedTask;
        }

        IsBusy = true;
        StatusMessage = "Creating an offline answer...";

        var answer = BuildOfflineAnswer(QuestionText.Trim());
        var item = new FaqItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Question = QuestionText.Trim(),
            Answer = answer,
            AskedBy = "Offline User",
            AskedAt = DateTime.UtcNow,
            IsAnswered = true
        };

        FaqItems.Insert(0, item);
        QuestionText = string.Empty;
        StatusMessage = "Saved locally.";
        IsBusy = false;
        return Task.CompletedTask;
    }

    private void LoadSeedItems()
    {
        FaqItems.Clear();
        FaqItems.Add(new FaqItem
        {
            Id = "faq-1",
            Question = "How do I use MC Offline Chat?",
            Answer = "Open the Chat tab to talk to your local model, the Documents tab to edit files, and the Stories tab to build worlds or characters.",
            AskedBy = "System",
            AskedAt = DateTime.UtcNow,
            IsAnswered = true
        });
        FaqItems.Add(new FaqItem
        {
            Id = "faq-2",
            Question = "Where are my stories and documents stored?",
            Answer = "Everything is stored locally on the device inside the app's private storage.",
            AskedBy = "System",
            AskedAt = DateTime.UtcNow,
            IsAnswered = true
        });
        FaqItems.Add(new FaqItem
        {
            Id = "faq-3",
            Question = "Can I use the app without the internet?",
            Answer = "Yes. Offline mode is the default and the app is designed to work with local GGUF models.",
            AskedBy = "System",
            AskedAt = DateTime.UtcNow,
            IsAnswered = true
        });
    }

    private static string BuildOfflineAnswer(string question)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("model"))
            return "Import a GGUF model in Settings, then select it from the local model picker.";
        if (lower.Contains("story") || lower.Contains("character") || lower.Contains("world"))
            return "Use the Stories tab to create a world, define a character, and attach an image from the gallery.";
        if (lower.Contains("document") || lower.Contains("code"))
            return "Use the Documents tab to create or import files, edit the content, and save it back locally.";
        if (lower.Contains("chat"))
            return "Open the Chat tab and start a conversation. The app will stay offline unless you import a model.";

        return "I stored that question locally. You can refine it further and I will help with a more specific answer.";
    }
}

public sealed class FaqItem
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string AskedBy { get; set; } = string.Empty;
    public DateTime AskedAt { get; set; }
    public bool IsAnswered { get; set; }
}
