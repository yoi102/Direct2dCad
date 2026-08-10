namespace Direct2dCad.ViewModels.Services.Platform;

public sealed record AiFileImportData(
    string SourceName,
    string ContentType,
    string? TextContent = null,
    string? DataUrl = null)
{
    public bool IsImage => !string.IsNullOrWhiteSpace(DataUrl);
}
