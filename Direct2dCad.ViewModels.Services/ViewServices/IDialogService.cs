namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IDialogService
{
    void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task ShowOrReplaceMessageInActiveWindowAsync(string header, string message, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);

    Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost);
}
