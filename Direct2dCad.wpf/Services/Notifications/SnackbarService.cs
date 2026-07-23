using System.Windows;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.wpf.Assists;
using MaterialDesignThemes.Wpf;

namespace Direct2dCad.wpf.Services.Notifications;

internal sealed class SnackbarService : ISnackbarService
{
    public void Enqueue(
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false)
    {
        Enqueue(ViewServiceIdentifiers.RootSnackbar, content, durationOverride, promote, neverConsiderToBeDuplicate);
    }

    public void EnqueueInAll(
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false)
    {
        InvokeOnUi(() =>
        {
            EnqueueCore(SnackbarIdentifierAssist.GetAllSnackbars(), content, durationOverride, promote, neverConsiderToBeDuplicate);
        });
    }

    public void Enqueue(
        object identifier,
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        InvokeOnUi(() =>
        {
            EnqueueCore(SnackbarIdentifierAssist.GetSnackbars(identifier), content, durationOverride, promote, neverConsiderToBeDuplicate);
        });
    }

    private static void EnqueueCore(
        IReadOnlyCollection<Snackbar> snackbars,
        object content,
        TimeSpan? durationOverride,
        bool promote,
        bool neverConsiderToBeDuplicate)
    {
        foreach (var snackbar in snackbars)
        {
            snackbar.MessageQueue?.Enqueue(
                content,
                null,
                null,
                null,
                promote,
                neverConsiderToBeDuplicate,
                durationOverride);
        }
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
}
