namespace Direct2dCad.ViewModels.Services.Platform;

public interface IDialogService
{
    void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);
    Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(
        string documentName,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);
    void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel);
    void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel);
}

public enum UnsavedDocumentDialogResult
{
    Save,
    Discard,
    Cancel
}
