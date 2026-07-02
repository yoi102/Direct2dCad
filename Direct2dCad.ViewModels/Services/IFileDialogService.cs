namespace Direct2dCad.ViewModels.Services;

public interface IFileDialogService
{
    string? SaveAsD2cad(string fileName);
    string? OpenD2cadFile();
}

