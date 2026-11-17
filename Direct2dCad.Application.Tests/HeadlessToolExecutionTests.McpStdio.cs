using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.Application.Tools;
using Direct2dCad.Mcp;
using Direct2dCad.Mcp.Stdio;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task McpStdioListsTheCatalogWithoutTheDesktopApp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new FakeLauncher();
        await using var bridge = await StdioBridge.StartAsync(Options(FreePort(), launch: false), launcher, timeout.Token);

        Assert.Equal(CadMcpToolCatalog.ServerName, bridge.Client.ServerInfo?.Name);
        Assert.Equal(CadMcpToolCatalog.ServerVersion, bridge.Client.ServerInfo?.Version);
        Assert.Equal(CadMcpToolCatalog.ServerInstructions, bridge.Client.ServerInstructions);
        var tools = await bridge.Client.ListToolsAsync(cancellationToken: timeout.Token);
        var expected = CadMcpToolCatalog.CreateTools();
        Assert.Equal(expected.Count, tools.Count);
        foreach (var tool in expected)
        {
            var listed = Assert.Single(tools, candidate => candidate.Name == tool.Name).ProtocolTool;
            Assert.True(JsonElement.DeepEquals(tool.InputSchema, listed.InputSchema));
            Assert.Equal(tool.Annotations?.ReadOnlyHint, listed.Annotations?.ReadOnlyHint);
            Assert.Equal(tool.Annotations?.DestructiveHint, listed.Annotations?.DestructiveHint);
            Assert.Equal(tool.Annotations?.OpenWorldHint, listed.Annotations?.OpenWorldHint);
        }
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task McpStdioForwardsCallsToTheRunningInstance()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        byte[] image = [5, 6, 7];
        workspace.CaptureHandler = (_, _, _) => Task.FromResult(new CadToolImage(image, "image/png", 1, 1));
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace,
            accessToken: "stdio-secret", cancellationToken: timeout.Token);
        var launcher = new FakeLauncher();
        await using var bridge = await StdioBridge.StartAsync(
            Options(server.Endpoint.Port, token: "stdio-secret"), launcher, timeout.Token);
        var client = bridge.Client;

        Assert.False((await client.CallToolAsync("create_document",
            new Dictionary<string, object?> { ["name"] = "Stdio \"引号\" \\ 図面" }, cancellationToken: timeout.Token)).IsError);
        for (var index = 1; index <= 2; index++)
        {
            var line = await client.CallToolAsync("add_line", new Dictionary<string, object?>
            {
                ["x1"] = 0, ["y1"] = 0, ["x2"] = index * 10, ["y2"] = 0
            }, cancellationToken: timeout.Token);
            Assert.False(line.IsError);
            Assert.NotNull(line.StructuredContent);
        }
        var document = Assert.Single(workspace.GetDocuments());
        Assert.Equal("Stdio \"引号\" \\ 図面", document.Name);
        Assert.Equal(2, document.Session.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        Assert.False((await client.CallToolAsync("undo", cancellationToken: timeout.Token)).IsError);
        Assert.Single(document.Session.CadEditor.Document.Entities.Values, entity => !entity.IsErased);

        var capture = await client.CallToolAsync("capture_view", cancellationToken: timeout.Token);
        Assert.Equal(image, Assert.Single(capture.Content.OfType<ImageContentBlock>()).DecodedData.ToArray());

        var invalid = await client.CallToolAsync("add_circle", new Dictionary<string, object?>
        {
            ["center_x"] = 0, ["center_y"] = 0, ["radius"] = -1
        }, cancellationToken: timeout.Token);
        Assert.True(invalid.IsError);

        var unknown = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("format_disk", cancellationToken: timeout.Token));
        Assert.Equal(McpErrorCode.InvalidParams, unknown.ErrorCode);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task McpStdioExplainsAMissingInstanceWhenLaunchingIsOff()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new FakeLauncher();
        await using var bridge = await StdioBridge.StartAsync(Options(FreePort(), launch: false), launcher, timeout.Token);

        var result = await bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
        Assert.True(result.IsError);
        Assert.Contains("not running", Text(result));
        Assert.Contains("--no-launch", Text(result));
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task McpStdioExplainsAMissingOrRejectedToken()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace,
            accessToken: "right-token", cancellationToken: timeout.Token);
        var launcher = new FakeLauncher();

        await using (var wrong = await StdioBridge.StartAsync(Options(server.Endpoint.Port, token: "wrong-token"), launcher, timeout.Token))
        {
            var result = await wrong.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
            Assert.True(result.IsError);
            Assert.Contains("rejected the MCP access token", Text(result));
            Assert.DoesNotContain("wrong-token", Text(result));
        }
        await using (var missing = await StdioBridge.StartAsync(Options(server.Endpoint.Port), launcher, timeout.Token))
        {
            var result = await missing.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
            Assert.True(result.IsError);
            Assert.Contains(CadMcpServer.AccessTokenEnvironmentVariable, Text(result));
        }
        Assert.Equal(0, launcher.LaunchCount);
        Assert.Equal(0, uiContext.DispatchCount);
    }

    [Fact]
    public async Task McpStdioStartsTheDesktopAppOnTheFirstCallOnly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        var port = FreePort();
        CadMcpServer? launched = null;
        var launcher = new FakeLauncher
        {
            OnLaunch = (launchPort, token) =>
            {
                // The app takes a moment to start listening, as the real window does.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    launched = await CadMcpServer.StartAsync(launchPort, uiContext, workspace, accessToken: token);
                });
            }
        };
        try
        {
            await using var bridge = await StdioBridge.StartAsync(Options(port, token: "launch-token"), launcher, timeout.Token);
            await bridge.Client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Equal(0, launcher.LaunchCount);

            var first = bridge.Client.CallToolAsync("create_document",
                new Dictionary<string, object?> { ["name"] = "Launched" }, cancellationToken: timeout.Token).AsTask();
            var second = bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token).AsTask();
            Assert.All(await Task.WhenAll(first, second), result => Assert.False(result.IsError, Text(result)));
            Assert.Equal(1, launcher.LaunchCount);
            Assert.Equal((port, "launch-token"), launcher.LastLaunch);
            Assert.Equal(1, launcher.LockCount);
            Assert.Equal("Launched", Assert.Single(workspace.GetDocuments()).Name);
        }
        finally
        {
            if (launched is not null)
                await launched.DisposeAsync();
        }
    }

    [Fact]
    public async Task McpStdioDoesNotOpenASecondWindow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new FakeLauncher { InstanceRunning = true };
        await using var bridge = await StdioBridge.StartAsync(Options(FreePort()), launcher, timeout.Token);

        var result = await bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
        Assert.True(result.IsError);
        Assert.Contains("Settings > MCP", Text(result));
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task McpStdioReportsAnAppThatExitsDuringStartup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new FakeLauncher { ExitImmediately = true };
        await using var bridge = await StdioBridge.StartAsync(Options(FreePort()), launcher, timeout.Token);

        var result = await bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
        Assert.True(result.IsError);
        Assert.Contains("exit code 3", Text(result));
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task McpStdioReconnectsAfterTheDesktopAppRestarts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var uiContext = new McpUiContext();
        using var firstWorkspace = new HeadlessWorkspace();
        var server = await CadMcpServer.StartAsync(0, uiContext, firstWorkspace, cancellationToken: timeout.Token);
        var port = server.Endpoint.Port;
        await using var bridge = await StdioBridge.StartAsync(Options(port, launch: false), new FakeLauncher(), timeout.Token);
        Assert.False((await bridge.Client.CallToolAsync("create_document",
            new Dictionary<string, object?> { ["name"] = "Before" }, cancellationToken: timeout.Token)).IsError);

        await server.DisposeAsync();
        using var secondWorkspace = new HeadlessWorkspace();
        await using var restarted = await CadMcpServer.StartAsync(port, uiContext, secondWorkspace, cancellationToken: timeout.Token);
        var after = await bridge.Client.CallToolAsync("create_document",
            new Dictionary<string, object?> { ["name"] = "After" }, cancellationToken: timeout.Token);

        Assert.False(after.IsError, Text(after));
        Assert.Equal("Before", Assert.Single(firstWorkspace.GetDocuments()).Name);
        Assert.Equal("After", Assert.Single(secondWorkspace.GetDocuments()).Name);
    }

    [Fact]
    public async Task McpStdioStartsTheDesktopAppAgainAfterItWasClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var uiContext = new McpUiContext();
        using var firstWorkspace = new HeadlessWorkspace();
        var server = await CadMcpServer.StartAsync(0, uiContext, firstWorkspace, cancellationToken: timeout.Token);
        var port = server.Endpoint.Port;
        using var secondWorkspace = new HeadlessWorkspace();
        CadMcpServer? relaunched = null;
        var launcher = new FakeLauncher
        {
            OnLaunch = (launchPort, token) => relaunched =
                CadMcpServer.StartAsync(launchPort, uiContext, secondWorkspace, accessToken: token).GetAwaiter().GetResult()
        };
        try
        {
            await using var closedBridge = await StdioBridge.StartAsync(Options(port, launch: false), launcher, timeout.Token);
            await using var bridge = await StdioBridge.StartAsync(Options(port), launcher, timeout.Token);
            Assert.False((await closedBridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token)).IsError);
            Assert.False((await bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token)).IsError);

            await server.DisposeAsync(); // The user closes the window between two calls.
            var refused = await closedBridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
            Assert.True(refused.IsError);
            Assert.Contains("not running", Text(refused));
            Assert.Equal(0, launcher.LaunchCount);

            var created = await bridge.Client.CallToolAsync("create_document",
                new Dictionary<string, object?> { ["name"] = "Reopened" }, cancellationToken: timeout.Token);
            Assert.False(created.IsError, Text(created));
            Assert.Equal(1, launcher.LaunchCount);
            Assert.Equal("Reopened", Assert.Single(secondWorkspace.GetDocuments()).Name);
            Assert.Empty(firstWorkspace.GetDocuments());
        }
        finally
        {
            if (relaunched is not null)
                await relaunched.DisposeAsync();
        }
    }

    [Fact]
    public async Task McpStdioRelaysCancellationToTheDesktopApp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Cancel");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            throw new InvalidOperationException("Unreachable.");
        };
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        await using var bridge = await StdioBridge.StartAsync(Options(server.Endpoint.Port), new FakeLauncher(), timeout.Token);

        using var call = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var capture = bridge.Client.CallToolAsync("capture_view", cancellationToken: call.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        await call.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        await cancelled.Task.WaitAsync(timeout.Token);

        var next = await bridge.Client.CallToolAsync("list_documents", cancellationToken: timeout.Token);
        Assert.False(next.IsError, Text(next));
    }

    [Fact]
    public async Task McpStdioRejectsDuplicatePropertyNamesLikeTheHttpListener()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        var input = new Pipe();
        var output = new Pipe();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var run = CadMcpStdioBridge.RunAsync(input.Reader.AsStream(), output.Writer.AsStream(),
            Options(server.Endpoint.Port), new FakeLauncher(), _ => { }, stop.Token);
        using var reader = new StreamReader(output.Reader.AsStream(), Encoding.UTF8);
        var writer = input.Writer.AsStream();

        async Task Send(string line) => await writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), timeout.Token);
        var received = new Dictionary<int, JsonNode>();
        async Task<JsonNode> Receive(int id)
        {
            // Parse errors are answered by the read loop, so they can overtake earlier requests.
            while (!received.ContainsKey(id))
            {
                var line = await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException();
                var message = JsonNode.Parse(line)!;
                if (message["id"] is { } messageId)
                    received[messageId.GetValue<int>()] = message;
            }
            return received[id];
        }

        await Send("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}}""");
        Assert.NotNull((await Receive(1))["result"]);
        await Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        // A duplicate inside a notification is dropped; a duplicate request gets an error for its id.
        await Send("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":1,"requestId":2}}""");
        await Send("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"create_document","arguments":{"name":"A","name":"B"}}}""");
        var rejected = await Receive(7);
        Assert.Equal(-32700, rejected["error"]!["code"]!.GetValue<int>());
        Assert.Empty(workspace.GetDocuments());

        // A bare CR splits messages for the SDK reader, so it splits them for the filter too.
        await Send("""{"jsonrpc":"2.0","id":8,"method":"ping"}""" + "\r" +
                   """{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"create_document","name":"undo"}}""");
        Assert.NotNull((await Receive(8))["result"]);
        Assert.Equal(-32700, (await Receive(9))["error"]!["code"]!.GetValue<int>());

        await Send("""{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"create_document","arguments":{"name":"Valid"}}}""");
        Assert.False((await Receive(10))["result"]!["isError"]?.GetValue<bool>() ?? false);
        Assert.Equal("Valid", Assert.Single(workspace.GetDocuments()).Name);

        await input.Writer.CompleteAsync();
        await run.WaitAsync(timeout.Token); // End of input ends the bridge.
    }

    [Fact]
    public void McpStdioOptionsUseTheDesktopArgumentsAndEnvironment()
    {
        Assert.True(CadMcpStdioBridge.IsRequested(["--mcp-stdio"]));
        Assert.False(CadMcpStdioBridge.IsRequested(["--mcp"]));

        var defaults = CadMcpStdioOptions.Parse(["--mcp-stdio"], _ => null);
        Assert.Equal(new CadMcpStdioOptions(CadMcpServer.DefaultPort, null, true, CadMcpStdioOptions.DefaultLaunchTimeout), defaults);

        var configured = CadMcpStdioOptions.Parse(["--mcp-stdio", "--mcp-port", "5390", "--no-launch"],
            name => name == CadMcpServer.AccessTokenEnvironmentVariable ? "env-token" : null);
        Assert.Equal(5390, configured.Port);
        Assert.Equal("env-token", configured.AccessToken);
        Assert.False(configured.Launch);
        Assert.Equal("arg-token", CadMcpStdioOptions.Parse(["--mcp-stdio", "--mcp-token", "arg-token"], _ => "env-token").AccessToken);

        Assert.Throws<ArgumentException>(() => CadMcpStdioOptions.Parse(["--mcp-stdio", "--mcp-prot", "5390"], _ => null));
        Assert.Throws<ArgumentException>(() => CadMcpStdioOptions.Parse(["--mcp-stdio", "--mcp-port", "0"], _ => null));
        Assert.Throws<ArgumentException>(() => CadMcpStdioOptions.Parse(["--mcp-stdio", "--mcp-token", "a b"], _ => null));
    }

    private static CadMcpStdioOptions Options(int port, string? token = null, bool launch = true) =>
        new(port, token, launch, TimeSpan.FromSeconds(10));

    private static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StdioBridge : IAsyncDisposable
    {
        private readonly Pipe _input = new();
        private readonly Pipe _output = new();
        private readonly CancellationTokenSource _stop = new();
        private Task _run = Task.CompletedTask;

        public McpClient Client { get; private set; } = null!;
        public System.Collections.Concurrent.ConcurrentQueue<string> Log { get; } = new();

        public static async Task<StdioBridge> StartAsync(
            CadMcpStdioOptions options, FakeLauncher launcher, CancellationToken cancellationToken)
        {
            var bridge = new StdioBridge();
            bridge._run = CadMcpStdioBridge.RunAsync(bridge._input.Reader.AsStream(), bridge._output.Writer.AsStream(),
                options, launcher, bridge.Log.Enqueue, bridge._stop.Token);
            bridge.Client = await McpClient.CreateAsync(
                new StreamClientTransport(bridge._input.Writer.AsStream(), bridge._output.Reader.AsStream()),
                cancellationToken: cancellationToken);
            return bridge;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _input.Writer.CompleteAsync();
            await _run.WaitAsync(TimeSpan.FromSeconds(10));
            _stop.Dispose();
        }
    }

    private sealed class FakeLauncher : ICadMcpInstanceLauncher
    {
        private int _launchCount;
        private int _lockCount;

        public bool InstanceRunning { get; init; }
        public bool ExitImmediately { get; init; }
        public Action<int, string?>? OnLaunch { get; init; }
        public int LaunchCount => Volatile.Read(ref _launchCount);
        public int LockCount => Volatile.Read(ref _lockCount);
        public (int Port, string? Token) LastLaunch { get; private set; }

        public bool IsInstanceRunning() => InstanceRunning;

        public Task<IDisposable> AcquireLaunchLockAsync(int port, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _lockCount);
            return Task.FromResult<IDisposable>(new MemoryStream());
        }

        public ICadLaunchedInstance Launch(int port, string? accessToken)
        {
            Interlocked.Increment(ref _launchCount);
            LastLaunch = (port, accessToken);
            OnLaunch?.Invoke(port, accessToken);
            return new Instance(ExitImmediately);
        }

        private sealed class Instance(bool exited) : ICadLaunchedInstance
        {
            public int ProcessId => 4242;
            public bool HasExited => exited;
            public int ExitCode => 3;
            public void Dispose() { }
        }
    }
}
