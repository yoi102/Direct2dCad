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
    Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(
        IReadOnlyList<UnsavedDocumentInfo> documents,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);
    Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(
        GridSpacingPresetDialogRequest request,
        string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost);
    void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel);
    void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel);
}

public enum UnsavedDocumentDialogResult
{
    Save,
    Discard,
    Cancel
}

public sealed record UnsavedDocumentInfo(string Name, string FilePath);

public sealed record GridSpacingPresetDialogRequest(
    bool IsEditing,
    string Name,
    double SpacingX,
    double SpacingY,
    bool LinkAxes,
    IReadOnlyList<string> UnavailableNames);

public sealed record GridSpacingPresetDialogResult(
    string Name,
    double SpacingX,
    double SpacingY,
    bool LinkAxes);

public enum GridSpacingPresetDialogAction
{
    Confirm,
    Cancel
}
