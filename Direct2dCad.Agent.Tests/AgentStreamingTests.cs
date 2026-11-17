using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Tests;

public sealed class AgentStreamingTests
{
    [Fact]
    public async Task RunAsync_ForwardsStreamedTextBeforeTheFinalMessage()
    {
        var client = new StreamingChatClient(["Hel", "lo"], yieldAfterDeltas: false);
        var events = new List<AgentRunEvent>();
        // Without a synchronization context (console hosts, MCP) deltas are reported inline and in order.
        // The test runner may install its own context, so clear it while the run captures it.
        var original = SynchronizationContext.Current;
        Task<AgentRunResult> run;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            run = new AgentRunner(client).RunAsync(
                CreateRequest(),
                agentEvent =>
                {
                    events.Add(agentEvent);
                    return ValueTask.CompletedTask;
                });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        await run;

        Assert.Collection(
            events,
            item => Assert.Equal((AgentRunEventKind.AssistantDelta, "Hel"), (item.Kind, item.Content)),
            item => Assert.Equal((AgentRunEventKind.AssistantDelta, "lo"), (item.Kind, item.Content)),
            item => Assert.Equal((AgentRunEventKind.AssistantMessage, "Hello"), (item.Kind, item.Content)));
    }

    [Fact]
    public async Task RunAsync_DoesNotRequestStreamingWithoutAnEventSink()
    {
        var client = new StreamingChatClient(["ignored"], yieldAfterDeltas: false);

        await new AgentRunner(client).RunAsync(CreateRequest());

        Assert.False(client.SawDeltaCallback);
    }

    [Fact]
    public async Task RunAsync_CoalescesDeltasOnTheCallersSynchronizationContext()
    {
        var client = new StreamingChatClient(["Hel", "lo", " world"], yieldAfterDeltas: true);
        var events = new List<AgentRunEvent>();
        var context = new QueueSynchronizationContext();
        var original = SynchronizationContext.Current;
        Task<AgentRunResult> run;
        int postsBeforePump;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            run = new AgentRunner(client).RunAsync(
                CreateRequest(),
                agentEvent =>
                {
                    Assert.Same(context, SynchronizationContext.Current);
                    events.Add(agentEvent);
                    return ValueTask.CompletedTask;
                });
            // Three pieces arrived before the UI thread ran: one flush plus the client's own yield.
            postsBeforePump = context.Count;
            context.RunAll();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        await run;
        Assert.Equal(2, postsBeforePump);
        Assert.Collection(
            events,
            item => Assert.Equal((AgentRunEventKind.AssistantDelta, "Hello world"), (item.Kind, item.Content)),
            item => Assert.Equal((AgentRunEventKind.AssistantMessage, "Hello world"), (item.Kind, item.Content)));
    }

    [Fact]
    public async Task RunAsync_DropsTextThatWasNotShownBeforeTheCompletionArrived()
    {
        var client = new StreamingChatClient(["late"], yieldAfterDeltas: false);
        var events = new List<AgentRunEvent>();
        var context = new QueueSynchronizationContext();
        var original = SynchronizationContext.Current;
        Task<AgentRunResult> run;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            run = new AgentRunner(client).RunAsync(
                CreateRequest(),
                agentEvent =>
                {
                    events.Add(agentEvent);
                    return ValueTask.CompletedTask;
                });
            context.RunAll();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        await run;
        // The final message already carries the text, so the stale flush must not add a second copy.
        var only = Assert.Single(events);
        Assert.Equal(AgentRunEventKind.AssistantMessage, only.Kind);
    }

    private static AgentRunRequest CreateRequest()
    {
        var conversation = new AgentConversation();
        conversation.AddUser("say hello");
        return new AgentRunRequest(
            "http://localhost",
            "test-model",
            "system prompt",
            "say hello",
            conversation,
            8192,
            0.2,
            null);
    }

    private sealed class StreamingChatClient(IReadOnlyList<string> deltas, bool yieldAfterDeltas) : IAiChatClient
    {
        public bool SawDeltaCallback { get; private set; }

        public Task<IReadOnlyList<string>> GetModelsAsync(
            string endpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public async Task<AiChatCompletion> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            SawDeltaCallback = request.OnContentDelta is not null;
            if (request.OnContentDelta is { } onDelta)
            {
                foreach (var delta in deltas)
                    await onDelta(delta);
            }
            if (yieldAfterDeltas)
                await Task.Yield();
            return new AiChatCompletion(string.Concat(deltas), [], request.Model);
        }
    }

    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();

        public int Count => _work.Count;

        public override void Post(SendOrPostCallback d, object? state) => _work.Enqueue((d, state));

        public void RunAll()
        {
            while (_work.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }
}
