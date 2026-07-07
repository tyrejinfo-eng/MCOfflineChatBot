using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.ViewModels;

public partial class StoriesViewModel : ObservableObject
{
    private readonly StoryLibraryService _service;
    private readonly WorkContextService _workContext;
    private readonly HistoryLibraryService _history;

    public ObservableCollection<StoryProfileItem> Stories { get; } = new();
    public ObservableCollection<StoryCharacterItem> Characters { get; } = new();
    public ObservableCollection<StoryChapterItem> Chapters { get; } = new();
    public ObservableCollection<CharacterItemEvent> ItemLogs { get; } = new();

    [ObservableProperty] private StoryProfileItem? _selectedStory;
    [ObservableProperty] private StoryCharacterItem? _selectedCharacter;
    [ObservableProperty] private StoryChapterItem? _selectedChapter;
    [ObservableProperty] private string _title = "Untitled Story";
    [ObservableProperty] private string _worldName = string.Empty;
    [ObservableProperty] private string _worldType = string.Empty;
    [ObservableProperty] private string _worldTime = string.Empty;
    [ObservableProperty] private string _worldInhabitants = string.Empty;
    [ObservableProperty] private string _terrainType = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;
    [ObservableProperty] private string _magicSystem = string.Empty;
    [ObservableProperty] private string _hardWorldRules = string.Empty;
    [ObservableProperty] private string _prologue = string.Empty;
    [ObservableProperty] private string _storySummary = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private string _statusText = "Create worlds, characters, and chapters offline.";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _characterName = string.Empty;
    [ObservableProperty] private string _characterSurname = string.Empty;
    [ObservableProperty] private string _characterSpecies = string.Empty;
    [ObservableProperty] private string _characterAge = string.Empty;
    [ObservableProperty] private string _characterStyle = string.Empty;
    [ObservableProperty] private string _characterTone = string.Empty;
    [ObservableProperty] private string _characterDescription = string.Empty;
    [ObservableProperty] private string _characterClothes = string.Empty;
    [ObservableProperty] private string _characterItemsText = string.Empty;
    [ObservableProperty] private string _characterMagicalAbilities = string.Empty;
    [ObservableProperty] private string _characterLikes = string.Empty;
    [ObservableProperty] private string _characterDislikes = string.Empty;
    [ObservableProperty] private string _characterFears = string.Empty;
    [ObservableProperty] private string _characterLocation = string.Empty;
    [ObservableProperty] private string _characterAvatarPath = string.Empty;
    [ObservableProperty] private string _itemEventAction = "acquired";
    [ObservableProperty] private string _itemEventName = string.Empty;
    [ObservableProperty] private string _itemEventNotes = string.Empty;
    [ObservableProperty] private string _chapterTitle = "Chapter 1";
    [ObservableProperty] private string _chapterContent = string.Empty;

    public StoriesViewModel(StoryLibraryService service, WorkContextService workContext, HistoryLibraryService history)
    {
        _service = service;
        _workContext = workContext;
        _history = history;
    }

    public async Task OnAppearingAsync()
    {
        await LoadAsync();
        await ApplyPendingOpenAsync();
    }

    partial void OnSelectedStoryChanged(StoryProfileItem? value)
    {
        if (value == null)
        {
            ClearEditor();
            return;
        }

        Title = value.Title;
        WorldName = value.WorldName;
        WorldType = value.WorldType;
        WorldTime = value.WorldTime;
        WorldInhabitants = value.WorldInhabitants;
        TerrainType = value.TerrainType;
        TagsText = value.TagsText;
        MagicSystem = value.MagicSystem;
        HardWorldRules = value.HardWorldRules;
        Prologue = value.Prologue;
        StorySummary = value.StorySummary;
        Notes = value.Notes;
        ImagePath = value.ImagePath;

        Characters.Clear();
        foreach (var character in value.Characters)
            Characters.Add(character);

        Chapters.Clear();
        foreach (var chapter in value.PreviousChapters)
            Chapters.Add(chapter);

        SelectedCharacter = Characters.FirstOrDefault();
        SelectedChapter = Chapters.FirstOrDefault();
    }

    partial void OnSelectedCharacterChanged(StoryCharacterItem? value)
    {
        if (value == null)
        {
            CharacterName = string.Empty;
            CharacterSurname = string.Empty;
            CharacterSpecies = string.Empty;
            CharacterAge = string.Empty;
            CharacterStyle = string.Empty;
            CharacterTone = string.Empty;
            CharacterDescription = string.Empty;
            CharacterClothes = string.Empty;
            CharacterItemsText = string.Empty;
            CharacterMagicalAbilities = string.Empty;
            CharacterLikes = string.Empty;
            CharacterDislikes = string.Empty;
            CharacterFears = string.Empty;
            CharacterLocation = string.Empty;
            CharacterAvatarPath = string.Empty;
            ItemLogs.Clear();
            return;
        }

        CharacterName = value.Name;
        CharacterSurname = value.Surname;
        CharacterSpecies = value.Species;
        CharacterAge = value.Age;
        CharacterStyle = value.Style;
        CharacterTone = value.Tone;
        CharacterDescription = value.Description;
        CharacterClothes = value.Clothes;
        CharacterItemsText = value.ItemsText;
        CharacterMagicalAbilities = value.MagicalAbilities;
        CharacterLikes = value.Likes;
        CharacterDislikes = value.Dislikes;
        CharacterFears = value.Fears;
        CharacterLocation = value.Location;
        CharacterAvatarPath = value.AvatarPath;
        ItemLogs.Clear();
        foreach (var log in value.ItemLog.OrderByDescending(x => x.TimestampUtc))
            ItemLogs.Add(log);
    }

    partial void OnSelectedChapterChanged(StoryChapterItem? value)
    {
        if (value == null)
        {
            ChapterTitle = "Chapter 1";
            ChapterContent = string.Empty;
            return;
        }

        ChapterTitle = value.Title;
        ChapterContent = value.Content;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Stories.Clear();
            foreach (var story in await _service.LoadAsync())
                Stories.Add(story);

            StatusText = Stories.Count == 0
                ? "No saved stories yet."
                : $"Loaded {Stories.Count} story item(s).";

            SelectedStory ??= Stories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to load stories: {ex.Message}";
            SglLogger.Warning("[Stories] Load failed: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewStoryAsync()
    {
        var item = await _service.CreateAsync();
        ApplyEditorToStory(item);
        await _service.SaveAsync(item);

        Stories.Insert(0, item);
        SelectedStory = item;
        StatusText = $"Created {item.DisplayTitle}.";
    }

    [RelayCommand]
    private async Task SaveStoryAsync()
    {
        if (SelectedStory == null)
        {
            await NewStoryAsync();
            return;
        }

        ApplyEditorToStory(SelectedStory);
        var savedId = SelectedStory.Id;
        await _service.SaveAsync(SelectedStory);
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == savedId);
        StatusText = $"Saved {SelectedStory?.DisplayTitle ?? SelectedStory?.Title}.";
    }

    [RelayCommand]
    private async Task ChooseImageAsync()
    {
        if (SelectedStory == null)
        {
            StatusText = "Create a story first.";
            return;
        }

        var photo = await MediaPicker.Default.PickPhotoAsync();
        if (photo == null)
            return;

        var storyId = SelectedStory.Id;
        var path = await _service.SetImageAsync(SelectedStory, photo);
        ImagePath = path ?? string.Empty;
        StatusText = "Story image added.";
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == storyId);
    }

    [RelayCommand]
    private async Task AddOrUpdateCharacterAsync()
    {
        if (SelectedStory == null)
        {
            StatusText = "Create a story first.";
            return;
        }

        var character = SelectedCharacter ?? new StoryCharacterItem();
        character.Name = CharacterName?.Trim() ?? string.Empty;
        character.Surname = CharacterSurname?.Trim() ?? string.Empty;
        character.Species = CharacterSpecies?.Trim() ?? string.Empty;
        character.Age = CharacterAge?.Trim() ?? string.Empty;
        character.Style = CharacterStyle?.Trim() ?? string.Empty;
        character.Tone = CharacterTone?.Trim() ?? string.Empty;
        character.Description = CharacterDescription?.Trim() ?? string.Empty;
        character.Clothes = CharacterClothes?.Trim() ?? string.Empty;
        character.ItemsText = CharacterItemsText?.Trim() ?? string.Empty;
        character.MagicalAbilities = CharacterMagicalAbilities?.Trim() ?? string.Empty;
        character.Likes = CharacterLikes?.Trim() ?? string.Empty;
        character.Dislikes = CharacterDislikes?.Trim() ?? string.Empty;
        character.Fears = CharacterFears?.Trim() ?? string.Empty;
        character.Location = CharacterLocation?.Trim() ?? string.Empty;
        character.AvatarPath = CharacterAvatarPath?.Trim() ?? string.Empty;

        if (!SelectedStory.Characters.Any(x => x.Id == character.Id))
            SelectedStory.Characters.Add(character);

        var storyId = SelectedStory.Id;
        var characterId = character.Id;

        SelectedStory.ModifiedUtc = DateTime.UtcNow;
        await _service.SaveAsync(SelectedStory);
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == storyId);
        SelectedCharacter = Characters.FirstOrDefault(x => x.Id == characterId) ?? Characters.FirstOrDefault();
        StatusText = $"Saved character {character.FriendlyName}.";
    }

    [RelayCommand]
    private async Task AddItemEventAsync()
    {
        if (SelectedStory == null || SelectedCharacter == null)
        {
            StatusText = "Select a character first.";
            return;
        }

        var storyId = SelectedStory.Id;
        var characterId = SelectedCharacter.Id;
        SelectedCharacter.AppendLog(ItemEventAction, ItemEventName, ItemEventNotes);
        SelectedStory.ModifiedUtc = DateTime.UtcNow;
        await _service.SaveAsync(SelectedStory);
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == storyId);
        SelectedCharacter = Characters.FirstOrDefault(x => x.Id == characterId);
        ItemEventName = string.Empty;
        ItemEventNotes = string.Empty;
        StatusText = $"Logged {ItemEventAction} for {SelectedCharacter?.FriendlyName}.";
    }

    [RelayCommand]
    private async Task AddCharacterAvatarAsync()
    {
        if (SelectedStory == null || SelectedCharacter == null)
        {
            StatusText = "Select a character first.";
            return;
        }

        var photo = await MediaPicker.Default.PickPhotoAsync();
        if (photo == null)
            return;

        var storyId = SelectedStory.Id;
        var characterId = SelectedCharacter.Id;
        await using var input = await photo.OpenReadAsync();
        var path = await _service.SetCharacterAvatarAsync(SelectedStory, SelectedCharacter, input, photo.FileName);
        CharacterAvatarPath = path ?? string.Empty;
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == storyId);
        SelectedCharacter = Characters.FirstOrDefault(x => x.Id == characterId);
        StatusText = "Character avatar updated.";
    }

    [RelayCommand]
    private async Task AddChapterAsync()
    {
        if (SelectedStory == null)
        {
            StatusText = "Create a story first.";
            return;
        }

        var chapter = new StoryChapterItem
        {
            Title = string.IsNullOrWhiteSpace(ChapterTitle) ? $"Chapter {SelectedStory.PreviousChapters.Count + 1}" : ChapterTitle.Trim(),
            Content = ChapterContent?.Trim() ?? string.Empty,
            ModifiedUtc = DateTime.UtcNow
        };

        SelectedStory.PreviousChapters.Add(chapter);
        SelectedStory.PreviousChapters = SelectedStory.PreviousChapters
            .OrderByDescending(x => x.ModifiedUtc)
            .Take(5)
            .OrderBy(x => x.CreatedUtc)
            .ToList();

        var storyId = SelectedStory.Id;
        await _service.SaveAsync(SelectedStory);
        await LoadAsync();
        SelectedStory = Stories.FirstOrDefault(x => x.Id == storyId);
        StatusText = "Chapter saved to metadata.";
    }

    [RelayCommand]
    private async Task DeleteStoryAsync()
    {
        if (SelectedStory == null)
            return;

        var target = SelectedStory;
        await _service.DeleteAsync(target);
        Stories.Remove(target);
        SelectedStory = Stories.FirstOrDefault();
        StatusText = $"Deleted {target.DisplayTitle}.";
    }

    private void ApplyEditorToStory(StoryProfileItem story)
    {
        story.Title = string.IsNullOrWhiteSpace(Title) ? "Untitled Story" : Title.Trim();
        story.WorldName = WorldName?.Trim() ?? string.Empty;
        story.WorldType = WorldType?.Trim() ?? string.Empty;
        story.WorldTime = WorldTime?.Trim() ?? string.Empty;
        story.WorldInhabitants = WorldInhabitants?.Trim() ?? string.Empty;
        story.TerrainType = TerrainType?.Trim() ?? string.Empty;
        story.TagsText = TagsText?.Trim() ?? string.Empty;
        story.MagicSystem = MagicSystem?.Trim() ?? string.Empty;
        story.HardWorldRules = HardWorldRules?.Trim() ?? string.Empty;
        story.Prologue = Prologue?.Trim() ?? string.Empty;
        story.StorySummary = StorySummary?.Trim() ?? string.Empty;
        story.Notes = Notes?.Trim() ?? string.Empty;
        story.ImagePath = ImagePath?.Trim() ?? string.Empty;
        story.Characters = Characters.ToList();
        story.PreviousChapters = Chapters.ToList();
        story.Normalize();
    }

    private async Task ApplyPendingOpenAsync()
    {
        var request = _workContext.Consume();
        if (request is not { } open || open.Target != WorkOpenTarget.Stories)
            return;

        var story = await _service.GetByIdAsync(open.Id);
        if (story == null)
        {
            StatusText = "Requested story could not be found.";
            return;
        }

        SelectedStory = story;
        StatusText = $"Opened {story.DisplayTitle} from history.";
    }

    private void ClearEditor()
    {
        Title = "Untitled Story";
        WorldName = string.Empty;
        WorldType = string.Empty;
        WorldTime = string.Empty;
        WorldInhabitants = string.Empty;
        TerrainType = string.Empty;
        TagsText = string.Empty;
        MagicSystem = string.Empty;
        HardWorldRules = string.Empty;
        Prologue = string.Empty;
        StorySummary = string.Empty;
        Notes = string.Empty;
        ImagePath = string.Empty;
        Characters.Clear();
        Chapters.Clear();
        ItemLogs.Clear();
    }
}
