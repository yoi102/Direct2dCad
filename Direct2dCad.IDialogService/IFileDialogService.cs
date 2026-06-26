namespace Direct2dCad.IDialogService;

public interface IFileDialogService
{
    string? SaveFile(string fileName);
    string? OpenFile();
}

