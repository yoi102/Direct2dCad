using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task WaitForExitDialogAsync(MainWindow window, Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 100 && !predicate(); attempt++)
        {
            window.UpdateLayout();
            await Task.Delay(10);
        }
        if (!predicate()) throw new InvalidOperationException("Application exit dialog did not settle.");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }

    private static void ClickExitDialogButton(DialogHost host, string resourceKey)
    {
        var button = ((Control)host.CurrentSession!.Content!).GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, CadUiText.Get(resourceKey)));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task CheckEmptyApplicationExitAsync(MainWindow window, NativeSmokeReport report, Action<bool, string> assert, string output)
    {
        var host = window.FindControl<DialogHost>("RootDialog")!;
        window.Close();
        var request = window.PendingExitConfirmation!;
        await WaitForExitDialogAsync(window, () => host.IsOpen);
        assert(window.IsVisible && !request.IsCompleted && ((Control)host.CurrentSession!.Content!).GetVisualDescendants()
            .OfType<TextBlock>().Any(t => t.Text == CadUiText.Get("ConfirmExitMessage")), "Closing an empty shell skipped the WPF exit confirmation.");
        var session = host.CurrentSession;
        window.Close();
        assert(ReferenceEquals(session, host.CurrentSession) && ReferenceEquals(request, window.PendingExitConfirmation), "Repeated Close replaced the pending exit confirmation.");
        if (report.HeadlessWindowing)
        {
            using var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            frame?.Save(Path.Combine(Path.GetDirectoryName(output)!, "exit-confirmation.png"));
            global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Key.Escape, global::Avalonia.Input.RawInputModifiers.None, global::Avalonia.Input.PhysicalKey.None, null);
            global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window, Key.Escape, global::Avalonia.Input.RawInputModifiers.None, global::Avalonia.Input.PhysicalKey.None, null);
        }
        else ClickExitDialogButton(host, "Cancel");
        await request;
        assert(window.IsVisible && !host.IsOpen && !window.Model.LayoutService.Documents.Any(), "Cancelling exit closed the shell or changed its documents.");
        report.Passed.Add("Window Close always shows WPF exit confirmation without drawings; repeated Close reuses the prompt and Cancel/Escape keeps the shell open");
        window.Close();
        request = window.PendingExitConfirmation!;
        await WaitForExitDialogAsync(window, () => host.IsOpen);
        ClickExitDialogButton(host, "Cancel");
        await request;
        assert(window.IsVisible && !host.IsOpen, "An exit cancellation prevented the next Close request from presenting a usable dialog.");
        report.Passed.Add("Cancelled exit can be retried and cancelled using the actual dialog button");
    }

    private static async Task CheckUnsavedApplicationExitAsync(MainWindow window, IServiceProvider services, NativeSmokeReport report, Action<bool, string> assert, string output)
    {
        var workspace = services.GetRequiredService<ICadToolWorkspace>();
        var directory = Path.GetDirectoryName(output)!;
        var index = 0;
        foreach (var document in workspace.GetDocuments())
            assert(await workspace.SaveDocumentAsync(document.DocumentId, Path.Combine(directory, $"exit-drawing-{index++}.d2cad"), default), "Could not prepare saved drawings for exit regression.");
        var tab = window.Model.CurrentEditorTabViewModel!;
        var editor = tab.CadDocumentViewModel.CadEditor;
        var circleId = editor.AddCircle(new CadPointD(321, 123), 7);
        assert(tab.IsModified, "Exit fixture did not dirty its saved drawing.");
        var host = window.FindControl<DialogHost>("RootDialog")!;

        window.Close();
        var request = window.PendingExitConfirmation!;
        await WaitForExitDialogAsync(window, () => host.IsOpen);
        var firstSession = host.CurrentSession;
        ClickExitDialogButton(host, "Confirm");
        await WaitForExitDialogAsync(window, () => host.IsOpen && !ReferenceEquals(firstSession, host.CurrentSession));
        assert(((Control)host.CurrentSession!.Content!).GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(tab.DocumentName, StringComparison.Ordinal) == true), "Confirmed exit did not show the modified drawing list.");
        if (report.HeadlessWindowing)
        {
            using var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            frame?.Save(Path.Combine(directory, "exit-unsaved.png"));
        }
        ClickExitDialogButton(host, "Cancel");
        await request;
        assert(window.IsVisible && tab.IsModified && editor.Document.GetEntity(circleId) is not null, "Cancelling the unsaved dialog closed the shell or lost changes.");
        report.Passed.Add("Confirmed application exit asks about unsaved drawings next; cancelling that dialog retains the window and modified geometry");

        // Exercise the same confirmation workflow's discard result before the final
        // real Close test; do not close the smoke window until its render checks finish.
        var discard = window.ConfirmApplicationExitAsync();
        await WaitForExitDialogAsync(window, () => host.IsOpen);
        firstSession = host.CurrentSession;
        ClickExitDialogButton(host, "Confirm");
        await WaitForExitDialogAsync(window, () => host.IsOpen && !ReferenceEquals(firstSession, host.CurrentSession));
        var savedBytes = File.ReadAllBytes(tab.CurrentFilePath);
        ClickExitDialogButton(host, "DontSave");
        assert(await discard && savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(tab.CurrentFilePath)), "Don't save rejected exit or wrote modified contents to disk.");
        report.Passed.Add("Exit confirmation followed by Don't save authorizes exit without changing the saved drawing");

        // Keep the smoke process alive after its real main-window Close, so it can
        // verify the saved data and serialize its report before explicit shutdown.
        var lifetime = (IClassicDesktopStyleApplicationLifetime)global::Avalonia.Application.Current!.ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        request = window.PendingExitConfirmation!;
        await WaitForExitDialogAsync(window, () => host.IsOpen);
        firstSession = host.CurrentSession;
        ClickExitDialogButton(host, "Confirm");
        await WaitForExitDialogAsync(window, () => host.IsOpen && !ReferenceEquals(firstSession, host.CurrentSession));
        var savedPath = tab.CurrentFilePath;
        ClickExitDialogButton(host, "Save");
        await request;
        var reloaded = await new CadDocumentStorage().LoadAsync(savedPath);
        assert(closed && !window.IsVisible && reloaded.GetEntity(circleId) is Direct2dCad.Db.Data.Entities.CadCircle { Radius: 7 }, "Save-and-exit did not close the real main window after persisting modified geometry.");
        report.Passed.Add("Exit confirmation followed by Save persists modified geometry, saves dock state and closes the actual main window");
    }
}
