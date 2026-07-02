using Direct2dCad.ViewModels.Services;
using Microsoft.Win32;

namespace Direct2dCad.wpf.Services;

internal class FileDialogService : IFileDialogService
{
    public string? SaveAsD2cad(string fileName)
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
    public string? OpenD2cadFile()
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
