using System.Windows;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Importing;

internal sealed class AiFileImportService(
    IImageImportService imageImportService) : IAiFileImportService
{
    public AiFileImportData Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        if (IsImageFile(filePath))
        {
            var image = imageImportService.LoadFromFile(filePath);
            return new AiFileImportData(
                image.SourceName,
                image.ContentType,
                DataUrl: imageImportService.CreatePngDataUrl(image));
        }

        return AiTextFileReader.Read(filePath);
    }

    public IReadOnlyList<AiFileImportData> LoadFilesFromClipboard()
    {
        if (!Clipboard.ContainsFileDropList())
            return [];

        return Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(Load)
            .ToArray();
    }

    private static bool IsImageFile(string filePath) =>
        filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

}
