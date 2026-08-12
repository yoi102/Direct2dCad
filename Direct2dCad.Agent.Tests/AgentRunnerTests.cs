using System.Net;
using System.Text.Json;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Tests;

public sealed class AgentRunnerTests
{
    [Fact]
    public async Task RunAsync_StoresAssistantResponseAndReportsIt()
    {
        var client = new QueuedChatClient([
            new AiChatCompletion("done", [], "test-model")
        ]);
        var conversation = ConversationWithUser("draw");
        var events = new List<AgentRunEvent>();

        var result = await new AgentRunner(client).RunAsync(
            CreateRequest(conversation),
            agentEvent =>
            {
                events.Add(agentEvent);
                return ValueTask.CompletedTask;
            });

        Assert.Equal("test-model", result.Model);
        Assert.False(result.ResponseWasEmpty);
        Assert.Equal(AiChatRole.Assistant, conversation.Messages[^1].Role);
        Assert.Equal("done", conversation.Messages[^1].Content);
        Assert.Contains(events, item =>
            item.Kind == AgentRunEventKind.AssistantMessage &&
            item.Content == "done");
    }

    [Fact]
    public async Task RunAsync_ExecutesToolsUntilModelReturnsFinalResponse()
    {
        var call = new AiToolCall("call-1", "inspect", "{}");
        var client = new QueuedChatClient([
            new AiChatCompletion(null, [call], "test-model"),
            new AiChatCompletion("finished", [], "test-model")
        ]);
        var toolset = new FakeToolset();
        var conversation = ConversationWithUser("inspect");

        var result = await new AgentRunner(client).RunAsync(
            CreateRequest(conversation, toolset));

        Assert.False(result.ResponseWasEmpty);
        Assert.Equal(1, toolset.ExecutionCount);
        Assert.Collection(
            conversation.Messages,
            message => Assert.Equal(AiChatRole.User, message.Role),
            message => Assert.Equal(AiChatRole.Assistant, message.Role),
            message =>
            {
                Assert.Equal(AiChatRole.Tool, message.Role);
                Assert.Equal(call.Id, message.ToolCallId);
            },
            message =>
            {
                Assert.Equal(AiChatRole.Assistant, message.Role);
                Assert.Equal("finished", message.Content);
            });
    }

    [Fact]
    public async Task RunAsync_ContextWindowFailureRetriesWithReportedLimit()
    {
        var client = new ContextRetryChatClient();
        var conversation = ConversationWithUser("inspect");
        var events = new List<AgentRunEvent>();

        var result = await new AgentRunner(client).RunAsync(
            CreateRequest(conversation),
            agentEvent =>
            {
                events.Add(agentEvent);
                return ValueTask.CompletedTask;
            });

        Assert.Equal(4096, result.ContextWindowTokens);
        Assert.Equal(2, client.CallCount);
        Assert.Contains(events, item =>
            item.Kind == AgentRunEventKind.ContextReduced &&
            item.ContextWindowTokens == 4096);
    }

    [Fact]
    public async Task RunAsync_ReportsFailureWhenRetryStillExceedsContextWindow()
    {
        var client = new AlwaysContextRetryChatClient();
        var events = new List<AgentRunEvent>();

        var exception = await Assert.ThrowsAsync<AiContextWindowExceededException>(() =>
            new AgentRunner(client).RunAsync(
                CreateRequest(ConversationWithUser("inspect")),
                agentEvent =>
                {
                    events.Add(agentEvent);
                    return ValueTask.CompletedTask;
                }));

        Assert.Contains("still too large", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, client.CallCount);
        Assert.Single(events, item => item.Kind == AgentRunEventKind.ContextReduced);
    }

    [Fact]
    public async Task RunAsync_RejectsInvalidRequestBeforeCallingClient()
    {
        var request = CreateRequest(ConversationWithUser("inspect")) with
        {
            MaximumToolRounds = 0
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new AgentRunner(new QueuedChatClient([])).RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_RejectsToolCallsWhenToolsetIsMissing()
    {
        var call = new AiToolCall("call-1", "inspect", "{}");
        var client = new QueuedChatClient([
            new AiChatCompletion(null, [call], "test-model")
        ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentRunner(client).RunAsync(CreateRequest(ConversationWithUser("inspect"))));

        Assert.Contains("no agent toolset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_StopsAfterMaximumToolRounds()
    {
        var call = new AiToolCall("call-1", "inspect", "{}");
        var client = new QueuedChatClient([
            new AiChatCompletion(null, [call], "test-model")
        ]);
        var request = CreateRequest(ConversationWithUser("inspect"), new FakeToolset()) with
        {
            MaximumToolRounds = 1
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentRunner(client).RunAsync(request));

        Assert.Contains("maximum number of agent tool rounds", exception.Message);
    }

    private static AgentConversation ConversationWithUser(string prompt)
    {
        var conversation = new AgentConversation();
        conversation.AddUser(prompt);
        return conversation;
    }

    private static AgentRunRequest CreateRequest(
        AgentConversation conversation,
        IAgentToolset? toolset = null) =>
        new(
            "http://localhost",
            "test-model",
            "system prompt",
            conversation.Messages[0].Content!,
            conversation,
            8192,
            0.2,
            toolset);

    private sealed class FakeToolset : IAgentToolset
    {
        private static readonly AiToolDefinition Tool = new(
            "inspect",
            "Inspect",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false
            }));

        public int ExecutionCount { get; private set; }
        public IReadOnlyList<AiToolDefinition> ToolDefinitions => [Tool];

        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) => [Tool];

        public Task<string> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult("""{"success":true}""");
        }
    }

    private sealed class QueuedChatClient(IEnumerable<AiChatCompletion> completions) : IAiChatClient
    {
        private readonly Queue<AiChatCompletion> _completions = new(completions);

        public Task<IReadOnlyList<string>> GetModelsAsync(
            string endpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<AiChatCompletion> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_completions.Dequeue());
    }

    private sealed class ContextRetryChatClient : IAiChatClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> GetModelsAsync(
            string endpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<AiChatCompletion> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new AiContextWindowExceededException(
                    "too large",
                    HttpStatusCode.BadRequest,
                    9000,
                    4096);
            }

            return Task.FromResult(new AiChatCompletion("done", [], request.Model));
        }
    }

    private sealed class AlwaysContextRetryChatClient : IAiChatClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> GetModelsAsync(
            string endpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<AiChatCompletion> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new AiContextWindowExceededException(
                "still too large",
                HttpStatusCode.BadRequest,
                9000,
                4096);
        }
    }
}
