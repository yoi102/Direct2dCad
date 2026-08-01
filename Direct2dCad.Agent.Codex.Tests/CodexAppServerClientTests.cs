using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Direct2dCad.AI;

namespace Direct2dCad.Agent.Codex.Tests;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public void CreateStartInfo_UsesBomlessUtf8ForStdio()
    {
        var startInfo = ProcessCodexAppServerTransport.CreateStartInfo(
            OperatingSystem.IsWindows() ? @"C:\Tools\codex.exe" : "/usr/bin/codex",
            "default");

        Assert.Empty(startInfo.StandardInputEncoding!.GetPreamble());
        Assert.Empty(startInfo.StandardOutputEncoding!.GetPreamble());
        Assert.Empty(startInfo.StandardErrorEncoding!.GetPreamble());
    }

    [Fact]
    public void CreateStartInfo_OnWindows_UsesCmdCommandLineWithoutDefaultServiceTier()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = ProcessCodexAppServerTransport.CreateStartInfo(
            @"C:\Program Files\Codex\codex.cmd",
            "default");

        Assert.Equal(
            Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Contains("/d /s /c", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains(
            "\"C:\\Program Files\\Codex\\codex.cmd\"",
            startInfo.Arguments,
            StringComparison.Ordinal);
        Assert.Contains("app-server --listen stdio://", startInfo.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("service_tier", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStartInfo_FastMode_InjectsFastServiceTier()
    {
        var startInfo = ProcessCodexAppServerTransport.CreateStartInfo(
            OperatingSystem.IsWindows() ? @"C:\Tools\codex.cmd" : "/usr/bin/codex",
            "fast");

        if (OperatingSystem.IsWindows())
            Assert.Contains("service_tier=\\\"fast\\\"", startInfo.Arguments, StringComparison.Ordinal);
        else
            Assert.Contains("service_tier=\"fast\"", startInfo.ArgumentList);
    }

    [Fact]
    public async Task GetModelsAsync_InitializesConnectionAndReturnsDistinctModels()
    {
        using var server = new FakeAppServer();
        server.RequestHandler = (method, id, _, transport) =>
        {
            transport.Reply(
                id,
                method switch
                {
                    "initialize" => new { },
                    "model/list" => new
                    {
                        data = new object[]
                        {
                            new { id = "gpt-5.3-codex" },
                            new { model = "gpt-5.3-codex" },
                            new { id = "gpt-5.2-codex" }
                        }
                    },
                    _ => new { }
                });
            return Task.CompletedTask;
        };
        using var client = server.CreateClient();

        var models = await client.GetModelsAsync(CreateOptions());

        Assert.Equal(["gpt-5.3-codex", "gpt-5.2-codex"], models);
        Assert.Contains(server.Messages, message => GetMethod(message) == "initialize");
        Assert.Contains(server.Messages, message => GetMethod(message) == "initialized");
        Assert.Contains(server.Messages, message => GetMethod(message) == "model/list");
    }

    [Fact]
    public async Task RunAsync_ExposesDynamicToolsAndReturnsToolResult()
    {
        using var server = new FakeAppServer();
        var toolset = new FakeToolset();
        var events = new List<AgentRunEvent>();
        server.RequestHandler = async (method, id, parameters, transport) =>
        {
            switch (method)
            {
                case "initialize":
                    transport.Reply(id, new { });
                    break;
                case "thread/start":
                    transport.Reply(id, new { thread = new { id = "thread-1" } });
                    break;
                case "turn/start":
                    transport.Reply(id, new { turn = new { id = "turn-1" } });
                    transport.Send(new
                    {
                        id = 900,
                        method = "item/tool/call",
                        @params = new
                        {
                            threadId = "thread-1",
                            turnId = "turn-1",
                            callId = "call-1",
                            @namespace = "direct2dcad",
                            tool = "inspect",
                            arguments = new { entityId = "entity-1" }
                        }
                    });
                    break;
                case "turn/interrupt":
                case "thread/unsubscribe":
                    transport.Reply(id, new { });
                    break;
            }

            await Task.CompletedTask;
        };
        server.ResponseHandler = (id, response, transport) =>
        {
            if (id != 900)
                return Task.CompletedTask;

            Assert.True(response.GetProperty("result").GetProperty("success").GetBoolean());
            transport.Send(new
            {
                method = "item/completed",
                @params = new
                {
                    item = new
                    {
                        type = "agentMessage",
                        text = "CAD entity inspected."
                    }
                }
            });
            transport.Send(new
            {
                method = "turn/completed",
                @params = new { turn = new { status = "completed" } }
            });
            return Task.CompletedTask;
        };
        using var client = server.CreateClient();

        var result = await client.RunAsync(
            new CodexAgentRunRequest(
                "inspect the entity",
                "workspace context",
                CreateOptions(),
                toolset),
            agentEvent =>
            {
                events.Add(agentEvent);
                return ValueTask.CompletedTask;
            });

        Assert.False(result.ResponseWasEmpty);
        Assert.Equal(1, toolset.ExecutionCount);
        Assert.Equal("entity-1", toolset.LastEntityId);
        Assert.Contains(events, item =>
            item.Kind == AgentRunEventKind.ToolResult &&
            item.ToolName == "inspect");
        Assert.Contains(events, item =>
            item.Kind == AgentRunEventKind.AssistantMessage &&
            item.Content == "CAD entity inspected.");

        var threadStart = server.Messages.Single(message => GetMethod(message) == "thread/start");
        var dynamicTool = threadStart
            .GetProperty("params")
            .GetProperty("dynamicTools")[0];
        Assert.Equal("inspect", dynamicTool.GetProperty("name").GetString());
        Assert.Equal("direct2dcad", dynamicTool.GetProperty("namespace").GetString());
        Assert.True(dynamicTool.GetProperty("deferLoading").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_RejectsToolCallsFromAnotherNamespace()
    {
        using var server = new FakeAppServer();
        var toolset = new FakeToolset();
        server.RequestHandler = (method, id, _, transport) =>
        {
            switch (method)
            {
                case "initialize":
                    transport.Reply(id, new { });
                    break;
                case "thread/start":
                    transport.Reply(id, new { thread = new { id = "thread-namespace" } });
                    break;
                case "turn/start":
                    transport.Reply(id, new { turn = new { id = "turn-namespace" } });
                    transport.Send(new
                    {
                        id = 901,
                        method = "item/tool/call",
                        @params = new
                        {
                            threadId = "thread-namespace",
                            turnId = "turn-namespace",
                            callId = "call-namespace",
                            @namespace = "another-client",
                            tool = "inspect",
                            arguments = new { entityId = "entity-1" }
                        }
                    });
                    break;
            }

            return Task.CompletedTask;
        };
        server.ResponseHandler = (id, response, transport) =>
        {
            if (id != 901)
                return Task.CompletedTask;

            var result = response.GetProperty("result");
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Contains(
                "Unsupported dynamic tool namespace",
                result.GetProperty("contentItems")[0].GetProperty("text").GetString());
            transport.Send(new
            {
                method = "turn/completed",
                @params = new { turn = new { status = "completed" } }
            });
            return Task.CompletedTask;
        };
        using var client = server.CreateClient();

        await client.RunAsync(
            new CodexAgentRunRequest(
                "inspect the entity",
                string.Empty,
                CreateOptions(),
                toolset));

        Assert.Equal(0, toolset.ExecutionCount);
    }

    [Fact]
    public async Task GetModelsAsync_PropagatesAppServerError()
    {
        using var server = new FakeAppServer();
        server.RequestHandler = (method, id, _, transport) =>
        {
            if (method == "initialize")
                transport.Reply(id, new { });
            else
                transport.ReplyError(id, -32602, "invalid model request");
            return Task.CompletedTask;
        };
        using var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetModelsAsync(CreateOptions()));

        Assert.Contains("invalid model request", exception.Message);
    }

    [Fact]
    public async Task RunAsync_CancellationInterruptsActiveTurn()
    {
        using var server = new FakeAppServer();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RequestHandler = (method, id, _, transport) =>
        {
            switch (method)
            {
                case "initialize":
                    transport.Reply(id, new { });
                    break;
                case "thread/start":
                    transport.Reply(id, new { thread = new { id = "thread-cancel" } });
                    break;
                case "turn/start":
                    transport.Reply(id, new { turn = new { id = "turn-cancel" } });
                    turnStarted.TrySetResult();
                    break;
                case "turn/interrupt":
                    transport.Reply(id, new { });
                    interrupted.TrySetResult();
                    break;
            }

            return Task.CompletedTask;
        };
        using var client = server.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var run = client.RunAsync(
            new CodexAgentRunRequest("wait", string.Empty, CreateOptions(), null),
            cancellationToken: cancellation.Token);
        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static CodexAgentOptions CreateOptions() =>
        new("codex", "gpt-5.3-codex", "medium", "default", Environment.CurrentDirectory);

    private static string? GetMethod(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;

    private sealed class FakeToolset : IAgentToolset
    {
        private static readonly AiToolDefinition Tool = new(
            "inspect",
            "Inspect a CAD entity.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    entityId = new { type = "string" }
                },
                required = new[] { "entityId" },
                additionalProperties = false
            }));

        public int ExecutionCount { get; private set; }
        public string? LastEntityId { get; private set; }
        public IReadOnlyList<AiToolDefinition> ToolDefinitions => [Tool];

        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) =>
            [Tool];

        public Task<string> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            using var arguments = JsonDocument.Parse(toolCall.ArgumentsJson);
            LastEntityId = arguments.RootElement.GetProperty("entityId").GetString();
            return Task.FromResult("""{"success":true,"entity_id":"entity-1"}""");
        }
    }

    private sealed class FakeAppServer : IDisposable
    {
        private readonly FakeTransport _transport = new();

        public Func<string, long, JsonElement, FakeTransport, Task>? RequestHandler
        {
            set => _transport.RequestHandler = value;
        }

        public Func<long, JsonElement, FakeTransport, Task>? ResponseHandler
        {
            set => _transport.ResponseHandler = value;
        }

        public IReadOnlyList<JsonElement> Messages => _transport.Messages;

        public CodexAppServerClient CreateClient() =>
            new(new FakeTransportFactory(_transport));

        public void Dispose() => _transport.Dispose();
    }

    private sealed class FakeTransportFactory(FakeTransport transport) : ICodexAppServerTransportFactory
    {
        public ICodexAppServerTransport Start(CodexAgentOptions options) => transport;
    }

    private sealed class FakeTransport : ICodexAppServerTransport
    {
        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
        private readonly ConcurrentQueue<JsonElement> _messages = new();
        private bool _disposed;

        public Func<string, long, JsonElement, FakeTransport, Task>? RequestHandler { get; set; }
        public Func<long, JsonElement, FakeTransport, Task>? ResponseHandler { get; set; }
        public IReadOnlyList<JsonElement> Messages => _messages.ToArray();

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _incoming.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(line);
            var message = document.RootElement.Clone();
            _messages.Enqueue(message);

            if (message.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString()!;
                if (message.TryGetProperty("id", out var idElement) &&
                    idElement.TryGetInt64(out var requestId) &&
                    RequestHandler is not null)
                {
                    var parameters = message.TryGetProperty("params", out var value)
                        ? value
                        : JsonSerializer.SerializeToElement(new { });
                    await RequestHandler(method, requestId, parameters, this);
                }

                return;
            }

            if (message.TryGetProperty("id", out var responseIdElement) &&
                responseIdElement.TryGetInt64(out var responseId) &&
                ResponseHandler is not null)
            {
                await ResponseHandler(responseId, message, this);
            }
        }

        public string GetErrorSummary() => string.Empty;

        public void Reply(long id, object result) =>
            Send(new { id, result });

        public void ReplyError(long id, int code, string message) =>
            Send(new { id, error = new { code, message } });

        public void Send(object message)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _incoming.Writer.TryWrite(JsonSerializer.Serialize(message));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _incoming.Writer.TryComplete();
        }
    }
}
