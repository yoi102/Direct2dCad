namespace Direct2dCad.ViewServices.Abstractions;

public interface IFileDialogService
{
    string? SaveFile(string fileName);
    string? OpenFile();
}

