using Direct2dCad.Client.Common;
using Direct2dCad.ViewModels.Services.ViewServices;
using Direct2dCad.wpf.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using System.Windows;

namespace Direct2dCad.wpf.Services;

internal sealed class DialogService : IDialogService
{
    public IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var identifier = NormalizeIdentifier(dialogIdentifier);

        InvokeOnUi(() =>
        {
            var progressDialog = new ProgressDialog();
            var dialogSession = DialogHost.GetDialogSession(identifier);

            if (dialogSession is not null)
            {
                dialogSession.UpdateContent(progressDialog);
            }
            else
            {
                _ = DialogHost.Show(progressDialog, identifier);
            }
        });

        return new DeferredScope(() => Close(identifier));
    }

    public void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var identifier = NormalizeIdentifier(dialogIdentifier);
        InvokeOnUi(() => CloseCurrentDialog(identifier));
    }

    public Task ShowOrReplaceMessageDialogAsync(
        string message,
        string header = "",
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        return ShowMessageAsync(header, message, dialogIdentifier);
    }

    public Task ShowOrReplaceMessageInActiveWindowAsync(
        string header,
        string message,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        return ShowMessageAsync(header, message, dialogIdentifier);
    }

    public async Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var result = await ShowReplacingCurrentAsync(() => new ExitConfirmDialog(), dialogIdentifier);

        return result is string resultString && resultString == bool.TrueString;
    }

    private static Task ShowMessageAsync(string header, string message, string dialogIdentifier)
    {
        return ShowReplacingCurrentAsync(() => new MessageDialog(header, message), dialogIdentifier);
    }

    private static Task<object?> ShowReplacingCurrentAsync(Func<object> dialogFactory, string dialogIdentifier)
    {
        var identifier = NormalizeIdentifier(dialogIdentifier);

        return InvokeOnUiAsync(async () =>
        {
            CloseCurrentDialog(identifier);
            await Task.Yield();
            return await DialogHost.Show(dialogFactory(), identifier);
        });
    }

    private static void CloseCurrentDialog(string identifier)
    {
        var dialogSession = DialogHost.GetDialogSession(identifier);
        dialogSession?.Close();
    }

    private static string NormalizeIdentifier(string? dialogIdentifier)
    {
        return string.IsNullOrWhiteSpace(dialogIdentifier)
            ? ViewServiceIdentifiers.RootDialogHost
            : dialogIdentifier;
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}
