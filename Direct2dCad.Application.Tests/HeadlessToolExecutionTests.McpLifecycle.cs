using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Direct2dCad.Application.Tools;
using Direct2dCad.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task McpLegacyCancellationNotificationStopsTheOriginalRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Legacy notification cancellation");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.SetResult(); }
            throw new InvalidOperationException("Legacy cancelled capture resumed.");
        };
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        using var observer = new McpRequestObserver();
        using var http = new HttpClient(observer);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint, TransportMode = HttpTransportMode.StreamableHttp
        }, http), new() { ProtocolVersion = "2025-11-25" }, cancellationToken: timeout.Token);
        var capture = client.CallToolAsync("capture_view", cancellationToken: timeout.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        var requestId = await observer.ToolRequestId.Task.WaitAsync(timeout.Token);
        using var notificationHttp = new HttpClient();
        notificationHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        notificationHttp.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        notificationHttp.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        if (client.SessionId is { } sessionId) notificationHttp.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);
        using var notification = new StringContent(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId, reason = "Client cancelled" }
        }), System.Text.Encoding.UTF8, "application/json");
        using var response = await notificationHttp.PostAsync(server.Endpoint, notification, timeout.Token);
        response.EnsureSuccessStatusCode();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
        await AssertMcpCallInterruptedAsync(capture);
    }

    private sealed class McpRequestObserver : DelegatingHandler
    {
        public TaskCompletionSource<JsonElement> ToolRequestId { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public McpRequestObserver() => InnerHandler = new SocketsHttpHandler();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                if (json.RootElement.TryGetProperty("method", out var method) && method.GetString() == "tools/call")
                    ToolRequestId.TrySetResult(json.RootElement.GetProperty("id").Clone());
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task McpRejectsUnknownToolsAndInvalidCursorsWithProtocolErrors(string version)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Protocol errors");
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        await using var client = await ConnectMcpAsync(server.Endpoint, version, timeout.Token);
        var unknown = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("missing_cad_tool", cancellationToken: timeout.Token).AsTask());
        Assert.Equal(McpErrorCode.InvalidParams, unknown.ErrorCode);
        var cursor = await Assert.ThrowsAsync<McpProtocolException>(() => client.ListToolsAsync(new ListToolsRequestParams { Cursor = "never-issued" }, timeout.Token).AsTask());
        Assert.Equal(McpErrorCode.InvalidParams, cursor.ErrorCode);
        Assert.Empty(document.Session.CadEditor.Document.Entities);
        Assert.False((await client.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token)).IsError);
    }

    private static Task<McpClient> ConnectMcpAsync(Uri endpoint, string version, CancellationToken token) =>
        McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp
        }), new() { ProtocolVersion = version }, cancellationToken: token);

    private static Dictionary<string, object?> LineArguments() => new()
    {
        ["x1"] = 0, ["y1"] = 0, ["x2"] = 10, ["y2"] = 5
    };

    private static async Task WaitForMcpConditionAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(10, token);
    }

    private static async Task AssertMcpCallInterruptedAsync(Task<CallToolResult> call)
    {
        try { Assert.True((await call).IsError); }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or McpException or IOException) { }
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task McpCancellationBeforeUiDispatchPreventsDelayedMutation(string version)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Pending cancellation");
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        await using var client = await ConnectMcpAsync(server.Endpoint, version, timeout.Token);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(_ => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(25)); }, null);
        await entered.Task.WaitAsync(timeout.Token);
        try
        {
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var abandoned = client.CallToolAsync("add_line", LineArguments(), cancellationToken: cancelled.Token).AsTask();
            await WaitForMcpConditionAsync(() => ui.PendingCount > 0, timeout.Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
            var next = client.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token).AsTask();
            // A second queued callback proves that the server observed cancellation and released its command gate.
            await WaitForMcpConditionAsync(() => ui.PendingCount >= 2, timeout.Token);
            release.Set();
            Assert.False((await next).IsError);
            Assert.Single(document.Session.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task McpCancelsActiveOperationsAndAcceptsSubsequentRequests(string version)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Active cancellation");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.SetResult(); }
            throw new InvalidOperationException("The cancelled capture resumed.");
        };
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        await using var client = await ConnectMcpAsync(server.Endpoint, version, timeout.Token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var capture = client.CallToolAsync("capture_view", cancellationToken: cancelled.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        await stopped.Task.WaitAsync(timeout.Token);
        Assert.False((await client.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token)).IsError);
        Assert.Single(document.Session.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
    }

    [Fact]
    public async Task McpHttpDisconnectCancelsTheStatelessOperation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Disconnected client");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.SetResult(); }
            throw new InvalidOperationException("Disconnected capture resumed.");
        };
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        using var http = new HttpClient();
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint, TransportMode = HttpTransportMode.StreamableHttp
        }, http), new() { ProtocolVersion = "2026-07-28" }, cancellationToken: timeout.Token);
        var capture = client.CallToolAsync("capture_view", cancellationToken: timeout.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        http.Dispose();
        await stopped.Task.WaitAsync(timeout.Token);
        await AssertMcpCallInterruptedAsync(capture);
        await using var replacement = await ConnectMcpAsync(server.Endpoint, "2026-07-28", timeout.Token);
        Assert.False((await replacement.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token)).IsError);
    }

    [Fact]
    public async Task McpShutdownCancelsActiveAndQueuedCallsAndReleasesThePort()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Shutdown");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.SetResult(); }
            throw new InvalidOperationException("Shutdown capture resumed.");
        };
        using var ui = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, ui, workspace, cancellationToken: timeout.Token);
        var endpoint = server.Endpoint;
        await using var client = await ConnectMcpAsync(endpoint, "2026-07-28", timeout.Token);
        var capture = client.CallToolAsync("capture_view", cancellationToken: timeout.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        var queued = client.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token).AsTask();
        await server.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        await stopped.Task.WaitAsync(timeout.Token);
        await AssertMcpCallInterruptedAsync(capture);
        await AssertMcpCallInterruptedAsync(queued);
        Assert.Empty(document.Session.CadEditor.Document.Entities);
        await using var replacement = await CadMcpServer.StartAsync(endpoint.Port, ui, workspace, cancellationToken: timeout.Token);
        await using var nextClient = await ConnectMcpAsync(replacement.Endpoint, "2026-07-28", timeout.Token);
        Assert.False((await nextClient.CallToolAsync("add_line", LineArguments(), cancellationToken: timeout.Token)).IsError);
    }

    [Fact]
    public async Task McpStartupFailurePreservesTheExistingListenerAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        using var ui = new McpUiContext();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => CadMcpServer.StartAsync(port, ui, workspace, cancellationToken: timeout.Token));
            using var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            Assert.True(connection.Connected);
        }
        finally { listener.Stop(); }
        await using var retry = await CadMcpServer.StartAsync(port, ui, workspace, cancellationToken: timeout.Token);
        await using var client = await ConnectMcpAsync(retry.Endpoint, "2026-07-28", timeout.Token);
        Assert.Equal(CadWorkspaceToolExecutor.ToolDefinitions.Count, (await client.ListToolsAsync(cancellationToken: timeout.Token)).Count);
    }
}
