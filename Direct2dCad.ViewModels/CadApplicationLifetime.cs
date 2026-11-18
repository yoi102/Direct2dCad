namespace Direct2dCad.ViewModels;

/// <summary>
/// The window-close protocol both desktop hosts follow: the first Close is cancelled and starts one confirmation (exit
/// prompt, then unsaved drawings); further Close requests while it runs reuse it; after the user accepts, the host
/// saves its layout, services stop, and the window is closed for real.
/// </summary>
public sealed class CadApplicationExit
{
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<Task> _shutdown;
    private bool _running;

    public CadApplicationExit(Func<Task<bool>> confirm, Func<Task> shutdown)
    {
        _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    /// <summary>True once the user accepted exiting; the host's next Close goes through.</summary>
    public bool IsAuthorized { get; private set; }

    /// <summary>True while the confirmation dialogs are up (hosts suppress window shortcuts meanwhile).</summary>
    public bool IsConfirming => _running;

    /// <summary>The most recent confirmation, kept after it completes.</summary>
    public Task? Pending { get; private set; }

    /// <summary>Call from the window's Closing event.</summary>
    /// <param name="beforeShutdown">Runs after the user confirmed, before services stop (the host saves its layout).</param>
    /// <param name="closeWindow">Closes the window again once exit is authorized.</param>
    /// <param name="onError">Reports a failure; the window stays open and Close can be retried.</param>
    /// <returns>True when the close may proceed; false when the host must cancel it.</returns>
    public bool OnClosing(Func<Task> beforeShutdown, Action closeWindow, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(beforeShutdown);
        ArgumentNullException.ThrowIfNull(closeWindow);
        ArgumentNullException.ThrowIfNull(onError);
        if (IsAuthorized)
            return true;
        if (!_running)
        {
            _running = true;
            Pending = RunAsync(beforeShutdown, closeWindow, onError);
        }
        return false;
    }

    private async Task RunAsync(Func<Task> beforeShutdown, Action closeWindow, Action<Exception> onError)
    {
        try
        {
            if (!await _confirm())
                return;
            await beforeShutdown();
            await _shutdown();
            IsAuthorized = true;
            closeWindow();
        }
        catch (Exception exception)
        {
            // Hosts call this from a Closing handler; an escaping exception would end the process.
            onError(exception);
        }
        finally
        {
            _running = false;
        }
    }
}

/// <summary>
/// Runs <c>work</c> every interval on the thread that called <see cref="Start"/> (the UI thread), never overlapping:
/// the next interval starts after the previous run finished, like a dispatcher timer stopped during its tick.
/// </summary>
public sealed class CadPeriodicWork : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<Task> _work;
    private readonly Action<Exception> _onError;
    private CancellationTokenSource? _cancellation;

    public CadPeriodicWork(TimeSpan interval, Func<Task> work, Action<Exception> onError)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _interval = interval;
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
    }

    public bool IsRunning => _cancellation is not null;

    /// <summary>Starts the loop; must be called on the UI thread so each run resumes there.</summary>
    public void Start()
    {
        if (_cancellation is not null)
            return;
        if (SynchronizationContext.Current is null)
            throw new InvalidOperationException("Start periodic work on the UI thread, so each run resumes on it.");
        _cancellation = new CancellationTokenSource();
        _ = RunAsync(_cancellation.Token);
    }

    public void Stop()
    {
        if (_cancellation is not { } cancellation)
            return;
        _cancellation = null;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try { await Task.Delay(_interval, cancellationToken); }
            catch (OperationCanceledException) { return; }
            if (cancellationToken.IsCancellationRequested)
                return;
            try { await _work(); }
            catch (Exception exception) { _onError(exception); }
        }
    }
}
