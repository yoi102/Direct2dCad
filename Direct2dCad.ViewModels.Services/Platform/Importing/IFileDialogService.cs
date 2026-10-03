namespace Direct2dCad.ViewModels.Services.Platform;

public interface IFileDialogService
{
    string? SaveAsD2cad(string fileName);
    string? ChooseCompatibleCopyPath(string fileName) => SaveAsD2cad(fileName);
    string? OpenD2cadFile();
    string? OpenFile();
    string? OpenImageFile();
    string? OpenDxfFile()=>null;
    string? ExportDxfFile(string fileName)=>null;
}

