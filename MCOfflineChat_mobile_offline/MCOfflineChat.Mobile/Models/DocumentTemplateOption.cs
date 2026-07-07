namespace MCOfflineChat.Mobile.Models;

public sealed class DocumentTemplateOption
{
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = ".txt";
    public string Language { get; set; } = "Text";
    public string StarterText { get; set; } = string.Empty;

    public string Display => $"{Name} ({Extension})";
    public override string ToString() => Display;
}
