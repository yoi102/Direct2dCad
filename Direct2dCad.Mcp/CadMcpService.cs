using Direct2dCad.Application.Platform;
using Direct2dCad.Application.Tools;
using MessagePipe;

namespace Direct2dCad.Mcp;

/// <summary>Serializes live configuration changes and owns the desktop HTTP listener.</summary>
public sealed class CadMcpService(
    SynchronizationContext uiContext,
    ICadToolWorkspace workspace,
    IImageImportService? imageImportService = null,
    IAsyncPublisher<CadToolActivityMessage>? activityPublisher = null) : ICadMcpService, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _configuration = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private CadMcpServer? _server;
    private CadMcpStatus _status = new(CadMcpState.Stopped);
    private readonly object _disposeLock = new();
    private Task? _disposeTask;

    public CadMcpStatus Status => Volatile.Read(ref _status);
    public event EventHandler? StatusChanged;

    public async Task ConfigureAsync(bool enabled, int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        try
        {
            await _configuration.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        try
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            if (enabled && _server?.Endpoint.Port == port)
                return;

            if (_server is { } previous)
            {
                SetStatus(new(CadMcpState.Stopping, previous.Endpoint));
                // A settings tool may reconfigure its own listener. Finish its HTTP response first.
                await previous.DrainToolResponsesAsync(_shutdown.Token).ConfigureAwait(false);
                _server = null;
                await previous.DisposeAsync().ConfigureAwait(false);
            }

            _shutdown.Token.ThrowIfCancellationRequested();
            if (!enabled)
            {
                SetStatus(new(CadMcpState.Stopped));
                return;
            }

            SetStatus(new(CadMcpState.Starting));
            _server = await CadMcpServer.StartAsync(port, uiContext, workspace,
                imageImportService, activityPublisher, _shutdown.Token).ConfigureAwait(false);
            SetStatus(new(CadMcpState.Running, _server.Endpoint));
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus(new(CadMcpState.Failed, Error: exception.GetBaseException().Message));
        }
        finally
        {
            _configuration.Release();
        }
    }

    private void SetStatus(CadMcpStatus status)
    {
        Volatile.Write(ref _status, status);
        uiContext.Post(_ => StatusChanged?.Invoke(this, EventArgs.Empty), null);
    }

    public void Dispose() => Task.Run(async () => await DisposeAsync()).GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            return new ValueTask(_disposeTask ??= StopAsync());
    }

    private async Task StopAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _configuration.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_server is { } server)
            {
                _server = null;
                await server.DisposeAsync().ConfigureAwait(false);
            }
            SetStatus(new(CadMcpState.Stopped));
        }
        finally
        {
            _configuration.Release();
        }
    }
}
