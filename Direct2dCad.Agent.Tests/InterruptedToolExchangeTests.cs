using System.Text.Json;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Tests;

public sealed class InterruptedToolExchangeTests
{
    [Theory]
    [InlineData("after-receipt")]
    [InlineData("after-return")]
    [InlineData("during-second")]
    [InlineData("second-fails")]
    [InlineData("before-tools")]
    public async Task NextRequestRetainsSuccessfulReceiptsAndExplicitPendingOutcomes(string interruption)
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new[] { new AiToolCall("first", "edit", "{}"), new AiToolCall("second", "edit", "{}"), new AiToolCall("third", "edit", "{}") };
        var client = new Client(new("starting", calls, "fake"));
        var tools = new Tools(async (call, token) =>
        {
            if (interruption == "after-return") cancellation.Cancel();
            if (call.Id == "second" && interruption == "during-second")
            { cancellation.Cancel(); await Task.Delay(1, token); }
            if (call.Id == "second" && interruption == "second-fails") throw new InvalidOperationException("Failed tool");
            return "{\"success\":true,\"entity_id\":42}";
        });
        var conversation = new AgentConversation();
        conversation.AddUser("Edit three entities");
        var request = new AgentRunRequest("http://fake", "fake", "Use tools", "Edit three entities", conversation, 32768, 0, tools);
        var runner = new AgentRunner(client);
        await Assert.ThrowsAnyAsync<Exception>(() => runner.RunAsync(request, ev =>
        {
            if ((interruption == "after-receipt" && ev.Kind == AgentRunEventKind.ToolResult) ||
                (interruption == "before-tools" && ev.Kind == AgentRunEventKind.AssistantMessage)) cancellation.Cancel();
            return ValueTask.CompletedTask;
        }, cancellation.Token));
        conversation.AddUser("Continue");
        await runner.RunAsync(request with { UserPrompt = "Continue" });
        var sent = client.Requests[1].Messages;
        var results = sent.Where(m => m.Role == AiChatRole.Tool).ToArray();
        Assert.Equal(3, results.Length);
        Assert.Equal(calls.Select(c => c.Id), results.Select(r => r.ToolCallId));
        Assert.Equal(interruption == "before-tools" ? 0 : interruption is "during-second" or "second-fails" ? 2 : 1, tools.Executed.Count);
        Assert.Equal(interruption != "before-tools", JsonDocument.Parse(results[0].Content!).RootElement.GetProperty("success").GetBoolean());
        var second = JsonDocument.Parse(results[1].Content!).RootElement;
        Assert.Equal(interruption is "during-second" or "second-fails" ? "execution_interrupted" : "not_executed", second.GetProperty("code").GetString());
        Assert.Equal(interruption is "during-second" or "second-fails", second.GetProperty("may_have_committed").GetBoolean());
        Assert.Equal("not_executed", JsonDocument.Parse(results[2].Content!).RootElement.GetProperty("code").GetString());
        var assistantIndex = sent.ToList().FindIndex(m => m.ToolCalls?.Count == 3);
        Assert.True(assistantIndex >= 0);
        Assert.All(sent.Skip(assistantIndex + 1).Take(3), m => Assert.Equal(AiChatRole.Tool, m.Role));
    }

    [Fact]
    public async Task CancellationPreservesCompletedCaptureImageAfterContiguousToolReplies()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new Client(new(null, [new("capture", "edit", "{}"), new("pending", "edit", "{}")], "fake"));
        var tools = new Tools((_, _) => Task.FromResult("""{"success":true,"result":{"image":{"mime_type":"image/png","width":1,"height":1,"data_base64":"AQIDBA=="}}}"""));
        var conversation = new AgentConversation();
        conversation.AddUser("Inspect drawing");
        var request = new AgentRunRequest("http://fake", "fake", "Use tools", "Inspect drawing", conversation, 32768, 0, tools);
        var runner = new AgentRunner(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(request, ev =>
        { if (ev.Kind == AgentRunEventKind.ToolResult) cancellation.Cancel(); return ValueTask.CompletedTask; }, cancellation.Token));
        conversation.AddUser("Continue");
        await runner.RunAsync(request with { UserPrompt = "Continue" });
        var sent = client.Requests[1].Messages;
        var image = Assert.Single(sent, m => m.IsToolResultAttachment);
        Assert.Contains(image.ContentParts!, p => p.Type == AiChatContentPartType.Image);
        var imageIndex = sent.ToList().IndexOf(image);
        Assert.Equal("capture", sent[imageIndex - 2].ToolCallId);
        Assert.Equal("pending", sent[imageIndex - 1].ToolCallId);
        Assert.Single(tools.Executed);
    }

    private sealed class Client(AiChatCompletion first) : IAiChatClient
    {
        public List<AiChatRequest> Requests { get; } = [];
        public Task<IReadOnlyList<string>> GetModelsAsync(string endpoint, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<AiChatCompletion> CompleteAsync(AiChatRequest request, CancellationToken token = default)
        { Requests.Add(request); return Task.FromResult(Requests.Count == 1 ? first : new AiChatCompletion("done", [], "fake")); }
    }
    private sealed class Tools(Func<AiToolCall, CancellationToken, Task<string>> execute) : IAgentToolset
    {
        public List<string> Executed { get; } = [];
        public IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } = [new("edit", "Edit", JsonSerializer.SerializeToElement(new { type = "object" }))];
        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) => ToolDefinitions;
        public Task<string> ExecuteAsync(AiToolCall call, CancellationToken token) { Executed.Add(call.Id); return execute(call, token); }
    }
}
