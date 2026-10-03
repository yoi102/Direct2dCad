namespace Direct2dCad.ViewModels.Services.Platform;

public interface IFileLocationService
{
    bool CanOpenContainingFolder(string? filePath);
    void OpenContainingFolder(string filePath);
}
