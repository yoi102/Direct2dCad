using Direct2dCad.IDialogService;
using Microsoft.Win32;

namespace Direct2dCad.wpf.DialogService;

internal class FileDialogService : IFileDialogService
{
    public string? SaveFile(string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Direct2dCad (*.d2cad)|*.d2cad|All files (*.*)|*.*",
            DefaultExt = ".d2cad",
            AddExtension = true,
            FileName = fileName
        };

        if (dialog.ShowDialog() != true)
            return null;

        return dialog.FileName;
    }
    public string? OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Direct2dCad (*.d2cad)|*.d2cad|All files (*.*)|*.*",
            DefaultExt = ".d2cad"
        };

        if (dialog.ShowDialog() != true)
            return null;

        return dialog.FileName;
    }
}
