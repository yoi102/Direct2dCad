using System.Net;
using System.Net.Http;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Mcp.Stdio;

/// <summary>
/// The bridge's connection to the desktop instance's loopback HTTP endpoint. It connects on the
/// first tool call, starts the app when allowed and nothing is listening, and reconnects after the
/// app restarts. All protocol and security rules stay in <see cref="CadMcpServer"/>.
/// </summary>
internal sealed class CadMcpStdioUpstream(
    CadMcpStdioOptions options,
    ICadMcpInstanceLauncher launcher,
    Action<string> log) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _connection = new(1, 1);
    private McpClient? _client;
    private bool _disposed;

    public Uri Endpoint { get; } = new($"http://127.0.0.1:{options.Port}/mcp");

    public async Task<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            McpClient client;
            try
            {
                client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (CadMcpUnavailableException exception)
            {
                return Failure(exception.Message);
            }

            try
            {
                return await client.CallToolAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (McpProtocolException)
            {
                throw; // The desktop app answered with a JSON-RPC error; relay it unchanged.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await DiscardAsync(client).ConfigureAwait(false);
                // Retry only failures that prove the call never ran: no connection could be opened (the
                // app was closed), or the session was unknown (the app restarted; the SDK normally
                // re-initializes by itself). Reconnecting may start the app again.
                if (attempt == 0 && (IsConnectFailure(exception) || FindStatus(exception) == HttpStatusCode.NotFound))
                {
                    log($"The Direct2dCad session at {Endpoint} ended ({Describe(exception)}); reconnecting.");
                    continue;
                }
                log($"Lost the connection to {Endpoint} during {request.Name}: {Describe(exception)}");
                return Failure($"The connection to Direct2dCad at {Endpoint} was lost while running {request.Name} " +
                    $"({Describe(exception)}). The tool may or may not have completed: inspect the drawing " +
                    "(for example with get_document_summary or list_entities) before retrying.");
            }
        }
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _client) is { } current)
            return current;
        await _connection.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is { } existing)
                return existing;
            var client = await ConnectOrLaunchAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _client, client);
            log($"Connected to Direct2dCad at {Endpoint}.");
            return client;
        }
        finally
        {
            _connection.Release();
        }
    }

    private async Task<McpClient> ConnectOrLaunchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CadMcpUnavailableException exception) when (exception.NotListening && options.Launch)
        {
        }

        // The lock is held until the started app answers, so a second bridge waiting here connects to
        // that app instead of mistaking its new window for an instance with MCP turned off.
        using var launchLock = await launcher.AcquireLaunchLockAsync(options.Port, cancellationToken).ConfigureAwait(false);
        try
        {
            return await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CadMcpUnavailableException exception) when (exception.NotListening)
        {
        }
        if (launcher.IsInstanceRunning())
            throw Unavailable($"Direct2dCad is running, but no MCP server is listening at {Endpoint}. " +
                $"Turn on MCP in Settings > MCP with port {options.Port}, then try again.");

        using var instance = launcher.Launch(options.Port, options.AccessToken);
        log($"Started Direct2dCad (process {instance.ProcessId}) with MCP on port {options.Port}; waiting for {Endpoint}.");
        var deadline = DateTime.UtcNow + options.LaunchTimeout;
        while (true)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            try
            {
                return await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (CadMcpUnavailableException exception) when (exception.NotListening)
            {
                if (instance.HasExited)
                    throw Unavailable($"Direct2dCad exited during startup (exit code {instance.ExitCode}).");
                if (DateTime.UtcNow >= deadline)
                    throw Unavailable($"Direct2dCad started, but its MCP server did not answer at {Endpoint} within " +
                        $"{options.LaunchTimeout.TotalSeconds:0} s. Check the Messages panel for an MCP error " +
                        "(for example, the port is already in use).");
            }
        }
    }

    private async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        // Loopback only: a system proxy must never see the token, and the app has no client timeout to obey.
        var http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = Endpoint,
            Name = CadMcpToolCatalog.ServerName,
            TransportMode = HttpTransportMode.StreamableHttp,
            // The app sends nothing unprompted, so a standing GET stream would only add a connection.
            EnableStandaloneGetStream = false,
            AdditionalHeaders = options.AccessToken is null
                ? null
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {options.AccessToken}" }
        }, http, loggerFactory: null, ownsHttpClient: true);
        try
        {
            return await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new() { Name = "Direct2dCad stdio bridge", Version = CadMcpToolCatalog.ServerVersion }
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Classify(exception);
        }
    }

    private CadMcpUnavailableException Classify(Exception exception)
    {
        var status = FindStatus(exception);
        if (status == HttpStatusCode.Unauthorized)
            return Unavailable(options.AccessToken is null
                ? $"Direct2dCad at {Endpoint} requires an MCP access token. Set the {CadMcpServer.AccessTokenEnvironmentVariable} " +
                  "environment variable in the MCP client configuration to the token shown in Settings > MCP."
                : $"Direct2dCad at {Endpoint} rejected the MCP access token. Copy the current token from Settings > MCP " +
                  $"into {CadMcpServer.AccessTokenEnvironmentVariable}.");
        if (status is null && IsConnectFailure(exception))
            return new CadMcpUnavailableException(
                $"Direct2dCad is not running, or its MCP server is off (nothing is listening at {Endpoint}). " +
                "Start Direct2dCad and turn on MCP in Settings > MCP" +
                (options.Launch ? "." : ", or remove --no-launch so the bridge can start it."),
                notListening: true);
        return Unavailable($"Could not connect to Direct2dCad at {Endpoint}: {Describe(exception)}");
    }

    private async Task DiscardAsync(McpClient client)
    {
        if (Interlocked.CompareExchange(ref _client, null, client) == client)
            await DisposeQuietlyAsync(client).ConfigureAwait(false);
    }

    private static async Task DisposeQuietlyAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection is already broken; there is nothing left to release cleanly.
        }
    }

    private static CallToolResult Failure(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };

    private static CadMcpUnavailableException Unavailable(string message) => new(message, notListening: false);

    private static bool IsConnectFailure(Exception exception) =>
        Find<HttpRequestException>(exception) is { StatusCode: null, HttpRequestError: HttpRequestError.ConnectionError };

    private static HttpStatusCode? FindStatus(Exception exception) => Find<HttpRequestException>(exception)?.StatusCode;

    private static T? Find<T>(Exception? exception) where T : Exception
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is T match)
                return match;
        }
        return null;
    }

    private static string Describe(Exception exception) =>
        FindStatus(exception) is { } status ? $"HTTP {(int)status} {status}" : exception.GetBaseException().Message;

    public async ValueTask DisposeAsync()
    {
        await _connection.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            if (Interlocked.Exchange(ref _client, null) is { } client)
                await DisposeQuietlyAsync(client).ConfigureAwait(false);
        }
        finally
        {
            _connection.Release();
        }
    }
}

internal sealed class CadMcpUnavailableException(string message, bool notListening) : Exception(message)
{
    /// <summary>Nothing accepted the connection, so starting the app may help.</summary>
    public bool NotListening { get; } = notListening;
}
