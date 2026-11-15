using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Direct2dCad.Application.Tools;
using Direct2dCad.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task McpDiscoversAndExecutesCadToolsWithIndependentUndo()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }), cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(CadWorkspaceToolExecutor.ToolDefinitions.Count, tools.Count);
        foreach (var definition in CadWorkspaceToolExecutor.ToolDefinitions)
        {
            var tool = Assert.Single(tools, candidate => candidate.Name == definition.Name);
            Assert.True(JsonElement.DeepEquals(definition.Parameters, tool.ProtocolTool.InputSchema));
        }

        var created = await client.CallToolAsync("create_document", new Dictionary<string, object?>
        {
            ["name"] = "MCP integration"
        }, cancellationToken: timeout.Token);
        Assert.False(created.IsError);
        for (var index = 1; index <= 2; index++)
        {
            var line = await client.CallToolAsync("add_line", new Dictionary<string, object?>
            {
                ["x1"] = 0, ["y1"] = 0, ["x2"] = index * 10, ["y2"] = 0
            }, cancellationToken: timeout.Token);
            Assert.False(line.IsError);
        }
        var document = Assert.Single(workspace.GetDocuments());
        Assert.Equal(2, document.Session.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        Assert.False((await client.CallToolAsync("undo", cancellationToken: timeout.Token)).IsError);
        Assert.Single(document.Session.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        Assert.False((await client.CallToolAsync("redo", cancellationToken: timeout.Token)).IsError);
        Assert.Equal(2, document.Session.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));

        var invalid = await client.CallToolAsync("add_circle", new Dictionary<string, object?>
        {
            ["center_x"] = 0, ["center_y"] = 0, ["radius"] = -1
        }, cancellationToken: timeout.Token);
        Assert.True(invalid.IsError);
        Assert.True(uiContext.DispatchCount >= 6);
    }

    [Fact]
    public async Task McpSerializesConcurrentEditsAndResolvesTheCurrentDocument()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        var first = workspace.CreateDocument("First");
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }), cancellationToken: timeout.Token);

        var second = workspace.CreateDocument("Second");
        var calls = Enumerable.Range(1, 8).Select(index => client.CallToolAsync("add_line",
            new Dictionary<string, object?>
            {
                ["x1"] = 0, ["y1"] = 0, ["x2"] = index, ["y2"] = 1
            }, cancellationToken: timeout.Token).AsTask());
        var results = await Task.WhenAll(calls);
        Assert.All(results, result => Assert.False(result.IsError));
        Assert.Empty(first.Session.CadEditor.Document.Entities);
        Assert.Equal(8, second.Session.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        Assert.False((await client.CallToolAsync("undo", cancellationToken: timeout.Token)).IsError);
        Assert.Equal(7, second.Session.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
    }

    [Fact]
    public async Task McpDeliversCapturedImagesAndRejectsForeignOrigins()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("Capture");
        byte[] image = [1, 2, 3, 4];
        workspace.CaptureHandler = (_, _, _) => Task.FromResult(new CadToolImage(image, "image/png", 2, 2));
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }), cancellationToken: timeout.Token);

        var capture = await client.CallToolAsync("capture_view", cancellationToken: timeout.Token);
        Assert.False(capture.IsError);
        var imageBlock = Assert.Single(capture.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.Equal(image, imageBlock.DecodedData.ToArray());
        Assert.DoesNotContain("data_base64", Assert.Single(capture.Content.OfType<TextContentBlock>()).Text);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint);
        request.Headers.Add("Origin", "https://example.com");
        using var response = await http.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("--mcp-port")]
    [InlineData("--mcp-port", "0")]
    [InlineData("--mcp-port", "65536")]
    [InlineData("--mcp-port", "invalid")]
    public void McpRejectsInvalidPortArguments(params string[] arguments) =>
        Assert.Throws<ArgumentException>(() => CadMcpServer.GetRequestedPort(arguments));

    [Fact]
    public async Task McpRejectsDuplicateArgumentNamesBeforeMutatingDocument()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Duplicate argument audit");
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        http.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        using var content = new StringContent("""
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"add_circle","arguments":{"center_x":0,"center_y":0,"radius":1,"radius":10}}}
            """, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(server.Endpoint, content, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.Empty(document.Session.CadEditor.Document.Entities);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpMarksInspectionToolsReadOnly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }), cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        foreach (var name in new[] { "list_documents", "get_entity_geometry", "measure_geometry", "capture_view" })
            Assert.True(Assert.Single(tools, tool => tool.Name == name).ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(Assert.Single(tools, tool => tool.Name == "delete_entities").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.All(tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true),
            tool => Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint));
        foreach (var name in new[] { "create_document", "create_layer", "add_circle", "add_entities", "insert_block" })
            Assert.False(Assert.Single(tools, tool => tool.Name == name).ProtocolTool.Annotations?.DestructiveHint);
        foreach (var name in new[] { "delete_entities", "save_document", "close_document", "transfer_entities", "set_entity_geometry" })
            Assert.True(Assert.Single(tools, tool => tool.Name == name).ProtocolTool.Annotations?.DestructiveHint);
    }

    [Fact]
    public async Task CapabilityExamplesMatchThePublishedToolSchemas()
    {
        using var workspace = new HeadlessWorkspace();
        var capabilities = await Execute(new(workspace), "get_agent_capabilities", new { });
        foreach (var example in capabilities.GetProperty("result").GetProperty("examples").EnumerateArray())
            CadToolSchemaValidator.NormalizeAndValidate(example.GetProperty("tool").GetString()!,
                example.GetProperty("arguments"), CadWorkspaceToolExecutor.ToolDefinitions);
    }

    [Fact]
    public void McpStartupIsExplicitAndAllowsACustomPort()
    {
        Assert.Null(CadMcpServer.GetRequestedPort([]));
        Assert.Equal(CadMcpServer.DefaultPort, CadMcpServer.GetRequestedPort(["--mcp"]));
        Assert.Equal(5280, CadMcpServer.GetRequestedPort(["--mcp-port", "5280", "--mcp"]));
    }

    private sealed class McpUiContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;
        private int _dispatchCount;
        public int DispatchCount => Volatile.Read(ref _dispatchCount);
        public int PendingCount => _queue.Count;

        public McpUiContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    Interlocked.Increment(ref _dispatchCount);
                    item.Callback(item.State);
                }
            }) { IsBackground = true, Name = "MCP test UI" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
        }
    }
}
