namespace Direct2dCad.ViewModels.Services.Platform;

public interface IClipboardTextService
{
    string? LoadFromClipboard();
    Task SetTextAsync(string text) => throw new NotSupportedException("Clipboard writing is unavailable.");
}
