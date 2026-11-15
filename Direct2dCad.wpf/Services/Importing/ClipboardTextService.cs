using System.Windows;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Importing;

internal sealed class ClipboardTextService : IClipboardTextService
{
    public Task SetTextAsync(string text)
    {
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        return Task.CompletedTask;
    }

    public string? LoadFromClipboard()
    {
        if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            return null;

        var text = Clipboard.GetText(TextDataFormat.UnicodeText);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
