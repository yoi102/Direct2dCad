using Direct2dCad.ViewModels.Services.Platform;
using Microsoft.Win32;

namespace Direct2dCad.wpf.Services.Importing;

internal class FileDialogService : IFileDialogService
{
    public string? OpenDxfFile()
    {var dialog=new OpenFileDialog{Filter="DXF (*.dxf)|*.dxf",DefaultExt=".dxf"};return dialog.ShowDialog()==true?dialog.FileName:null;}
    public string? ExportDxfFile(string fileName)
    {var dialog=new SaveFileDialog{Filter="DXF R2000 (*.dxf)|*.dxf",DefaultExt=".dxf",AddExtension=true,FileName=fileName,OverwritePrompt=false};return dialog.ShowDialog()==true?dialog.FileName:null;}
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
    public string? ChooseCompatibleCopyPath(string fileName)
    {
        var dialog=new SaveFileDialog {Filter="Direct2dCad (*.d2cad)|*.d2cad",DefaultExt=".d2cad",AddExtension=true,FileName=fileName,OverwritePrompt=false};
        return dialog.ShowDialog()==true ? dialog.FileName : null;
    }

    public string? OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files (*.*)|*.*",
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() != true)
            return null;

        return dialog.FileName;
    }
}
