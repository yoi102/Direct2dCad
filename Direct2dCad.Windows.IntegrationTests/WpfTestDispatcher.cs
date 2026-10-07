using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Direct2dCad.Windows.IntegrationTests;

/// <summary>
/// One Application and STA dispatcher for tests that require an application resource scope or
/// real WPF windows. Application.Shutdown is process-wide: a later Window.Show in the same test
/// process cannot create a visible HWND after an earlier test shuts the Application down.
/// </summary>
internal static class WpfTestDispatcher
{
    private static readonly Lazy<Dispatcher> Shared = new(CreateDispatcher);

    internal static void Run(Action action)
    {
        var dispatcher = Shared.Value;
        dispatcher.Invoke(() =>
        {
            var application = System.Windows.Application.Current!;
            var existingWindows = application.Windows.Cast<Window>().ToHashSet();
            try { action(); }
            finally
            {
                // A failed focus assertion must not leave a visible test window behind.
                foreach (var window in application.Windows.Cast<Window>().Where(window => !existingWindows.Contains(window)).ToArray())
                    window.Close();
                Keyboard.ClearFocus();
                dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(30));
    }

    private static Dispatcher CreateDispatcher()
    {
        var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Startup += (_, _) => started.TrySetResult(application.Dispatcher);
                application.Run();
            }
            catch (Exception exception)
            {
                if (!started.TrySetException(exception)) throw;
            }
        }) { IsBackground = true, Name = "Direct2dCad WPF integration tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // This background host lives until the test process exits; shutting it down between
        // tests would permanently disable window creation for the remainder of that process.
        return started.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }
}
