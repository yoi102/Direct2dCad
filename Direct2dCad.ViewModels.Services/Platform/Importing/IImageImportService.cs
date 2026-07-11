namespace Direct2dCad.ViewModels.Services.Platform;

public interface IImageImportService
{
    CadImageImportData LoadFromFile(string filePath);
    CadImageImportData? LoadFromClipboard();
}
