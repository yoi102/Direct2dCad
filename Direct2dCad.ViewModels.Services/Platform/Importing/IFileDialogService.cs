namespace Direct2dCad.ViewModels.Services.Platform;

public interface IFileDialogService
{
    string? SaveAsD2cad(string fileName);
    string? OpenD2cadFile();
    string? OpenFile();
    string? OpenImageFile();
}

