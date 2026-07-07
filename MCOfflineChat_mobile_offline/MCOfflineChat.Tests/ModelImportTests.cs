using MCOfflineChat.Mobile.Services;
using Xunit;

namespace MCOfflineChat.Tests;

public sealed class ModelImportTests
{
    [Fact]
    public async Task ImportModelFromPath_CopiesGgufIntoModelFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mcoffline-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var sourcePath = Path.Combine(tempRoot, "draft-model.gguf");
        await File.WriteAllTextAsync(sourcePath, "gguf-test-content");

        var knowledge = new ThreatKnowledgeService(Path.Combine(tempRoot, "knowledge"));
        using var service = new MobileLlmService(knowledge, Path.Combine(tempRoot, "models"), autoLoadOnImport: false);

        var result = await service.ImportModelFromPathAsync(sourcePath, "draft-model.gguf");

        Assert.True(result);
        Assert.Contains("draft-model", service.GetDownloadedModels());
        var importedPath = Path.Combine(tempRoot, "models", "draft-model.gguf");
        Assert.True(File.Exists(importedPath));
        Assert.Equal("gguf-test-content", await File.ReadAllTextAsync(importedPath));
    }
}
