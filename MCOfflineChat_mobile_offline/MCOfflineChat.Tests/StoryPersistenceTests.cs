using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Mobile.Services;
using Xunit;

namespace MCOfflineChat.Tests;

public sealed class StoryPersistenceTests
{
    [Fact]
    public async Task Story_Metadata_Characters_Chapters_And_Image_Persist()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mcoffline-story-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var history = new HistoryLibraryService(Path.Combine(tempRoot, "history"));
        var service = new StoryLibraryService(Path.Combine(tempRoot, "stories"), history);

        var story = await service.CreateAsync();
        story.Title = "Sky Harbor";
        story.WorldName = "Asterfall";
        story.WorldType = "Another planet";
        story.WorldTime = "Futuristic";
        story.WorldInhabitants = "Aliens";
        story.TerrainType = "Space";
        story.TagsText = "scifi, drama, action";
        story.MagicSystem = "Neon sigils";
        story.HardWorldRules = "No time travel.";
        story.Prologue = "The sky broke open.";
        story.Characters.Add(new StoryCharacterItem { Name = "Nova", Species = "Human", Description = "Pilot" });
        story.PreviousChapters.Add(new StoryChapterItem { Title = "Chapter 1", Content = "Opening scene" });

        await using var image = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        await service.SetImageAsync(story, image, "cover.png");
        await service.SaveAsync(story);

        var loaded = await service.GetByIdAsync(story.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Sky Harbor", loaded!.Title);
        Assert.Single(loaded.Characters);
        Assert.Single(loaded.PreviousChapters);
        Assert.True(File.Exists(loaded.ImagePath));
        Assert.Equal(1, service.Count());

        await service.DeleteAsync(loaded);
        Assert.Equal(0, service.Count());
    }
}
