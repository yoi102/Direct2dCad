namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IImageImportService
{
    CadImageImportData LoadFromFile(string filePath);
    CadImageImportData? LoadFromClipboard();
}
