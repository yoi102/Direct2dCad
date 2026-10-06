namespace Direct2dCad.Application.Platform;

public interface IImageImportService
{
    CadImageImportData LoadFromFile(string filePath);
    CadImageImportData? LoadFromClipboard();
    string CreatePngDataUrl(CadImageImportData image);
}
