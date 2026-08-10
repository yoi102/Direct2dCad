namespace Direct2dCad.ViewModels.Services.Platform;

public interface IAiFileImportService
{
    AiFileImportData Load(string filePath);
    IReadOnlyList<AiFileImportData> LoadFilesFromClipboard();
}
