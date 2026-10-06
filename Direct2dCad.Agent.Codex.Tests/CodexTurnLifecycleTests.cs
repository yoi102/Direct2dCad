using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Codex.Tests;

public sealed class CodexTurnLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static CodexAgentOptions Options => new("fake-codex", "test-model", "medium", "default", Environment.CurrentDirectory);
    private static CodexAgentRunRequest Request(IAgentToolset? tools = null) => new("inspect", "", Options, tools);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Cancellation_CancelsToolAndWaitsForExitBeforeReleasingRunGate()
    {
        using var server = new Server();
        var tool = new DelayedTool();
        server.StartTurn = (id, n) => { server.Reply(id, new { turn = new { id = $"turn-{n}" } }); server.Tool(100, "turn-1"); };
        using var client = new CodexAppServerClient(new Factory(server));
        using var cancellation = new CancellationTokenSource();
        var events = new ConcurrentQueue<AgentRunEvent>();
        var run = client.RunAsync(Request(tool), e => { events.Enqueue(e); return ValueTask.CompletedTask; }, cancellation.Token);
        await tool.Started.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        await server.Interrupted.Task.WaitAsync(Timeout);

        Assert.True(tool.Token.IsCancellationRequested);
        Assert.False(run.IsCompleted);
        Assert.Equal("turn-1", server.InterruptParams.GetProperty("turnId").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync(Request()));
        tool.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.False(tool.Committed);
        Assert.DoesNotContain(events, e => e.Kind == AgentRunEventKind.ToolResult);
        Assert.False((await server.Replies.Reader.ReadAsync().AsTask().WaitAsync(Timeout)).GetProperty("result").GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("thread-1", "turn-1")]
    [InlineData("other-thread", "turn-2")]
    public async Task LateEventsAndTools_CannotAffectCurrentTurn(string staleThread, string staleTurn)
    {
        using var server = new Server();
        var starts = new[] { Signal(), Signal() };
        server.StartTurn = (id, n) => { server.Reply(id, new { turn = new { id = $"turn-{n}" } }); starts[n - 1].TrySetResult(); };
        using var client = new CodexAppServerClient(new Factory(server));
        using var cancel = new CancellationTokenSource();
        var first = client.RunAsync(Request(), cancellationToken: cancel.Token);
        await starts[0].Task.WaitAsync(Timeout);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
        var tool = new CountingTool();
        var events = new ConcurrentQueue<AgentRunEvent>();
        var second = client.RunAsync(Request(tool), e => { events.Enqueue(e); return ValueTask.CompletedTask; });
        await starts[1].Task.WaitAsync(Timeout);
        server.Message(staleTurn, "stale answer", staleThread);
        server.Complete(staleTurn, staleThread);
        server.Tool(200, staleTurn, staleThread);
        var rejected = await server.Replies.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.False(rejected.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.False(second.IsCompleted);
        server.Message("turn-2", "current answer");
        server.Complete("turn-2");
        await second.WaitAsync(Timeout);
        Assert.Equal(0, tool.Count);
        Assert.Equal("current answer", Assert.Single(events).Content);
    }

    [Fact]
    public async Task NotificationsBeforeStartReply_AreBufferedWithoutBlockingResponseReader()
    {
        using var server = new Server();
        server.StartTurn = (id, _) =>
        {
            server.Message("turn-1", "early answer");
            server.Complete("turn-1");
            server.Reply(id, new { turn = new { id = "turn-1" } });
        };
        using var client = new CodexAppServerClient(new Factory(server));
        var events = new ConcurrentQueue<AgentRunEvent>();
        var result = await client.RunAsync(Request(), e => { events.Enqueue(e); return ValueTask.CompletedTask; }).WaitAsync(Timeout);
        Assert.False(result.ResponseWasEmpty);
        Assert.Equal("early answer", Assert.Single(events).Content);
    }

    [Fact]
    public async Task ReaderFailure_ClosesConnectionAndNextRequestReinitializes()
    {
        using var first = new Server();
        using var second = new Server();
        first.StartTurn = (id, _) => { first.Reply(id, new { turn = new { id = "turn-1" } }); first.SendRaw("invalid-json"); };
        var factory = new Factory(first, second);
        using var client = new CodexAppServerClient(factory);
        await Assert.ThrowsAnyAsync<Exception>(() => client.RunAsync(Request()).WaitAsync(Timeout));
        var models = await client.GetModelsAsync(Options).WaitAsync(Timeout);
        Assert.Equal(["test-model"], models);
        Assert.Equal(2, factory.Starts);
        Assert.True(first.Disposed);
        Assert.Equal(1, second.Initializations);
    }

    [Fact]
    public async Task CancellationBeforeStartReply_ClosesUnownedTurnConnection()
    {
        using var first = new Server();
        using var second = new Server();
        var started = Signal();
        first.StartTurn = (_, _) => started.TrySetResult();
        var factory = new Factory(first, second);
        using var client = new CodexAppServerClient(factory);
        using var cancel = new CancellationTokenSource();
        var run = client.RunAsync(Request(), cancellationToken: cancel.Token);
        await started.Task.WaitAsync(Timeout);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.True(first.Disposed);
        Assert.Equal(["test-model"], await client.GetModelsAsync(Options).WaitAsync(Timeout));
        Assert.Equal(2, factory.Starts);
    }

    [Fact]
    public async Task Dispose_CompletesRunWaitingForProvider()
    {
        using var server = new Server();
        var started = Signal();
        server.StartTurn = (id, _) => { server.Reply(id, new { turn = new { id = "turn-1" } }); started.TrySetResult(); };
        var client = new CodexAppServerClient(new Factory(server));
        var run = client.RunAsync(Request());
        await started.Task.WaitAsync(Timeout);
        client.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(Timeout));
        Assert.True(server.Disposed);
    }

    [Fact]
    public async Task ToolImage_IsSentAsImageContentAndNotLoggedAsBase64()
    {
        using var server = new Server();
        server.StartTurn = (id, _) => { server.Reply(id, new { turn = new { id = "turn-1" } }); server.Tool(300, "turn-1"); };
        using var client = new CodexAppServerClient(new Factory(server));
        var events = new ConcurrentQueue<AgentRunEvent>();
        var tool = new CountingTool { Result = """{"success":true,"result":{"image":{"mime_type":"image/png","data_base64":"aGVsbG8=","width":1,"height":1}}}""" };
        var run = client.RunAsync(Request(tool), e => { events.Enqueue(e); return ValueTask.CompletedTask; });
        var reply = await server.Replies.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        var content = reply.GetProperty("result").GetProperty("contentItems");
        Assert.Equal("inputImage", content[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,aGVsbG8=", content[1].GetProperty("imageUrl").GetString());
        Assert.DoesNotContain("data_base64", content[0].GetProperty("text").GetString());
        server.Complete("turn-1");
        await run.WaitAsync(Timeout);
        Assert.DoesNotContain("aGVsbG8=", Assert.Single(events).Content);
    }

    private sealed class DelayedTool : CountingTool
    {
        public TaskCompletionSource Started = Signal(), Release = Signal();
        public CancellationToken Token;
        public bool Committed;
        public override async Task<string> ExecuteAsync(AiToolCall call, CancellationToken token)
        {
            Token = token; Started.TrySetResult(); await Release.Task;
            token.ThrowIfCancellationRequested(); Committed = true; return Result;
        }
    }

    private class CountingTool : IAgentToolset
    {
        public int Count;
        public string Result = "{\"success\":true}";
        public IReadOnlyList<AiToolDefinition> ToolDefinitions => [new("mutate", "test", JsonSerializer.SerializeToElement(new { type = "object" }))];
        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) => ToolDefinitions;
        public virtual Task<string> ExecuteAsync(AiToolCall call, CancellationToken token) { Count++; return Task.FromResult(Result); }
    }

    private sealed class Factory(params Server[] servers) : ICodexAppServerTransportFactory
    {
        public int Starts;
        public ICodexAppServerTransport Start(CodexAgentOptions options) => servers[Starts++];
    }

    private sealed class Server : ICodexAppServerTransport
    {
        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
        public Action<long, int>? StartTurn;
        private int _turns;
        public int Initializations;
        public bool Disposed;
        public JsonElement InterruptParams;
        public TaskCompletionSource Interrupted = Signal();
        public Channel<JsonElement> Replies = Channel.CreateUnbounded<JsonElement>();
        public async Task<string?> ReadLineAsync(CancellationToken token)
        {
            try { return await _incoming.Reader.ReadAsync(token); } catch (ChannelClosedException) { return null; }
        }
        public Task WriteLineAsync(string line, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var rid)) return Task.CompletedTask;
            var id = rid.GetInt64();
            if (!root.TryGetProperty("method", out var method)) { Replies.Writer.TryWrite(root.Clone()); return Task.CompletedTask; }
            switch (method.GetString())
            {
                case "initialize": Initializations++; Reply(id, new { }); break;
                case "thread/unsubscribe": Reply(id, new { }); break;
                case "turn/interrupt":
                    InterruptParams = root.GetProperty("params").Clone();
                    Assert.False(string.IsNullOrWhiteSpace(InterruptParams.GetProperty("turnId").GetString()));
                    Reply(id, new { }); Interrupted.TrySetResult(); break;
                case "thread/start": Reply(id, new { thread = new { id = "thread-1" } }); break;
                case "turn/start": StartTurn!(id, ++_turns); break;
                case "model/list": Reply(id, new { data = new[] { new { model = "test-model" } } }); break;
            }
            return Task.CompletedTask;
        }
        public string GetErrorSummary() => "";
        public void Reply(long id, object result) => Send(new { id, result });
        public void Send(object value) => SendRaw(JsonSerializer.Serialize(value));
        public void SendRaw(string value) => _incoming.Writer.TryWrite(value);
        public void Tool(long id, string turnId, string threadId = "thread-1") => Send(new { id, method = "item/tool/call", @params = new { threadId, turnId, @namespace = "direct2dcad", tool = "mutate", callId = $"call-{id}", arguments = new { } } });
        public void Message(string turnId, string text, string threadId = "thread-1") => Send(new { method = "item/completed", @params = new { threadId, turnId, item = new { type = "agentMessage", text } } });
        public void Complete(string turnId, string threadId = "thread-1") => Send(new { method = "turn/completed", @params = new { threadId, turn = new { id = turnId, status = "completed" } } });
        public void Dispose() { Disposed = true; _incoming.Writer.TryComplete(); }
    }
}
