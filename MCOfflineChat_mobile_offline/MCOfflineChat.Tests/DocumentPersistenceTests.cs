using MCOfflineChat.Mobile.Services;
using Xunit;

namespace MCOfflineChat.Tests;

public sealed class DocumentPersistenceTests
{
    [Fact]
    public async Task Import_Save_Load_And_Delete_Document_Works()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mcoffline-doc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var history = new HistoryLibraryService(Path.Combine(tempRoot, "history"));
        var service = new LocalDocumentService(Path.Combine(tempRoot, "documents"), history);

        await using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("console.log('offline');"));
        var imported = await service.ImportAsync(source, "script.ts");

        Assert.Equal(".ts", imported.Extension);
        Assert.Equal("TypeScript", imported.Language);
        Assert.True(File.Exists(imported.FilePath));
        Assert.Equal(1, service.Count());

        var loaded = await service.LoadAsync();
        Assert.Single(loaded);
        Assert.Equal(imported.Id, loaded[0].Id);

        await service.DeleteAsync(imported);
        Assert.Equal(0, service.Count());
    }
}
