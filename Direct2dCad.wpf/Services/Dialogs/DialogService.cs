using System.Windows;
using Direct2dCad.Client.Common;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Blocks;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.wpf.Views.Dialogs;
using Direct2dCad.wpf.Views.Settings.DocumentSettings;
using Direct2dCad.wpf.Views.Settings.UserSettings;
using MaterialDesignThemes.Wpf;

namespace Direct2dCad.wpf.Services.Dialogs;

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

    public async Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(
        string message,
        string header = "",
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var result = await ShowMessageAsync(header, message, dialogIdentifier, MessageDialogButton.OKCancel);
        return result is string resultString && resultString == bool.TrueString;
    }

    public async Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var header = Strings.ConfirmExitTitle;
        var message = Strings.ConfirmExitMessage;
        var buttonType = MessageDialogButton.OKCancel;
        var width = 350;
        var okButtonContent = Strings.Confirm;

        MessageDialog messageDialog = new(header, message, buttonType);
        messageDialog.Width = width;
        messageDialog.SetOKButtonContent(okButtonContent);
        var result = await ShowReplacingCurrentAsync(() => messageDialog, dialogIdentifier);

        return result is string resultString && resultString == bool.TrueString;
    }

    public async Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(
        string documentName,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var result = await ShowReplacingCurrentAsync(
            () => new UnsavedDocumentDialog(documentName),
            dialogIdentifier);
        return result is UnsavedDocumentDialogResult dialogResult
            ? dialogResult
            : UnsavedDocumentDialogResult.Cancel;
    }

    public async Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(
        IReadOnlyList<UnsavedDocumentInfo> documents,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            return UnsavedDocumentDialogResult.Discard;

        var result = await ShowReplacingCurrentAsync(
            () => new UnsavedDocumentsDialog(documents),
            dialogIdentifier);
        return result is UnsavedDocumentDialogResult dialogResult
            ? dialogResult
            : UnsavedDocumentDialogResult.Cancel;
    }

    public async Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(
        GridSpacingPresetDialogRequest request,
        string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = new GridSpacingPresetEditorViewModel(request);
        var result = await ShowReplacingCurrentAsync(
            () => new GridSpacingPresetDialog { DataContext = viewModel },
            dialogIdentifier);
        return result is GridSpacingPresetDialogAction.Confirm && viewModel.IsValid
            ? viewModel.CreateResult()
            : null;
    }

    public async Task<CreateBlockDialogResult?> ShowCreateBlockDialogAsync(
        CreateBlockDialogRequest request,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = new CreateBlockDialogViewModel(request);
        var result = await ShowReplacingCurrentAsync(
            () => new CreateBlockDialog { DataContext = viewModel },
            dialogIdentifier);
        return result is CreateBlockDialogAction.Confirm && viewModel.IsValid
            ? viewModel.CreateResult()
            : null;
    }

    public async Task<AiAssistantSettingsDialogResult?> ShowAiAssistantSettingsDialogAsync(
        AiAssistantSettingsDialogRequest request,
        string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = new AiAssistantSettingsDialogViewModel(request);
        var result = await ShowReplacingCurrentAsync(
            () => new AiAssistantSettingsDialog { DataContext = viewModel },
            dialogIdentifier);
        return result is AiAssistantSettingsDialogAction.Confirm && viewModel.IsValid
            ? viewModel.CreateResult()
            : null;
    }

    private static Task<object?> ShowMessageAsync(string header, string message, string dialogIdentifier, MessageDialogButton buttonType = MessageDialogButton.OK)
    {
        MessageDialog messageDialog = new(header, message, buttonType);

        return ShowReplacingCurrentAsync(() => messageDialog, dialogIdentifier);
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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    public void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InvokeOnUi(() =>
        {
            var settingsDialog = new DocumentSettingsDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                DataContext = viewModel
            };
            settingsDialog.ShowDialog();
        });
    }

    public void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InvokeOnUi(() =>
        {
            var settingsDialog = new UserSettingsDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                DataContext = viewModel
            };
            settingsDialog.ShowDialog();
        });
    }
}
