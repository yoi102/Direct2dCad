using System.Text.Json;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Tests;

public sealed class AgentToolDiscoveryTests
{
    [Fact]
    public async Task DiscoveryLoadsPreviouslyUnselectedToolsOnTheNextRound()
    {
        var tools = new Toolset();
        var client = new Client((request, index) => Task.FromResult(index switch
        {
            0 => new AiChatCompletion(null, [new("find", "discover_tools", """{"names":["new_future_tool"]}""")], null),
            1 => new AiChatCompletion(null, [new("execute", "new_future_tool", "{}")], null),
            _ => new AiChatCompletion("done", [], null)
        }));
        await new AgentRunner(client).RunAsync(Request("inspect", tools));
        Assert.Contains(client.Requests[0].Tools, tool => tool.Name == "discover_tools");
        Assert.DoesNotContain(client.Requests[0].Tools, tool => tool.Name == "new_future_tool");
        Assert.Contains(client.Requests[1].Tools, tool => tool.Name == "new_future_tool");
        Assert.Equal(new[] { "new_future_tool" }, tools.Executed);
    }

    [Fact]
    public async Task ContinueUsesPreviousUserIntentAndActualTools()
    {
        var request = Request("继续", new Toolset());
        request.Conversation.Clear();
        request.Conversation.AddUser("draw circle");
        request.Conversation.AddAssistant(new(null, [new("earlier", "new_future_tool", "{}")], null));
        request.Conversation.AddToolResult(new("earlier", "new_future_tool", "{}"), "{}");
        request.Conversation.AddUser("继续");
        var client = new Client((_, _) => Task.FromResult(new AiChatCompletion("done", [], null)));
        await new AgentRunner(client).RunAsync(request);
        Assert.Contains(client.Requests[0].Tools, tool => tool.Name == "add_circle");
        Assert.Contains(client.Requests[0].Tools, tool => tool.Name == "new_future_tool");
    }

    [Theory]
    [InlineData("{\"names\":[\"missing\"]}")]
    [InlineData("{\"names\":\"add_circle\"}")]
    [InlineData("{\"query\":5}")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"names\":[\"add_circle\"],\"query\":false}")]
    [InlineData("{\"query\":\"inspect\",\"query\":\"circle\"}")]
    public void InvalidDiscoveryDoesNotExecuteTools(string arguments)
    {
        var tools = new Toolset();
        var result = AgentToolDiscovery.Execute(tools, arguments);
        using var json = JsonDocument.Parse(result.Result);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(result.Tools);
        Assert.Empty(tools.Executed);
    }

    [Fact]
    public void DiscoveryListsNewCatalogMembersWithoutASecondRegistry()
    {
        var result = AgentToolDiscovery.Execute(new Toolset(), "{}");
        Assert.Contains("new_future_tool", result.Result);
        Assert.Empty(result.Tools);
    }

    [Fact]
    public async Task CancellationSuppressesLateCompletionAndMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<AiChatCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, _) => pending.Task);
        var tools = new Toolset();
        var events = new List<AgentRunEvent>();
        var task = new AgentRunner(client).RunAsync(Request("draw circle", tools), item => { events.Add(item); return ValueTask.CompletedTask; }, cancellation.Token);
        cancellation.Cancel();
        pending.SetResult(new("late success", [new("late", "add_circle", "{}")], null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(events);
        Assert.Empty(tools.Executed);
    }

    [Fact]
    public async Task CancellationSuppressesLateToolSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new Toolset { Execute = _ => pending.Task };
        var client = new Client((_, _) => Task.FromResult(new AiChatCompletion(null, [new("late", "add_circle", "{}")], null)));
        var events = new List<AgentRunEvent>();
        var request = Request("draw circle", tools);
        var task = new AgentRunner(client).RunAsync(request, item => { events.Add(item); return ValueTask.CompletedTask; }, cancellation.Token);
        cancellation.Cancel();
        pending.SetResult("late success");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.DoesNotContain(events, item => item.Kind == AgentRunEventKind.ToolResult);
        Assert.DoesNotContain(request.Conversation.Messages, item => item.Content == "late success");
    }

    [Fact]
    public async Task CaptureImagesFollowAllToolRepliesAndStayOutOfTextEvents()
    {
        var tools = new Toolset { Execute = name => Task.FromResult(name == "capture_view" ?
            """{"success":true,"result":{"image":{"mime_type":"image/png","data_base64":"AQID","width":1,"height":1}}}""" : "{}") };
        var client = new Client((_, index) => Task.FromResult(index == 0 ?
            new AiChatCompletion(null, [new("image", "capture_view", "{}"), new("other", "inspect", "{}")], null) :
            new AiChatCompletion("done", [], null)));
        var events = new List<AgentRunEvent>();
        var request = Request("capture", tools);
        await new AgentRunner(client).RunAsync(request, item => { events.Add(item); return ValueTask.CompletedTask; });
        Assert.All(events, item => Assert.DoesNotContain("AQID", item.Content ?? ""));
        var sent = client.Requests[1].Messages;
        Assert.Contains(sent, message => message.ContentParts?.Any(part => part.Type == AiChatContentPartType.Image && part.DataUrl!.EndsWith("AQID")) == true);
        var imageIndex = request.Conversation.Messages.ToList().FindIndex(message => message.ContentParts?.Any(part => part.Type == AiChatContentPartType.Image) == true);
        Assert.Equal("other", request.Conversation.Messages[imageIndex - 1].ToolCallId);
        Assert.Equal("image", request.Conversation.Messages[imageIndex - 2].ToolCallId);
    }

    private static AgentRunRequest Request(string prompt, IAgentToolset tools)
    {
        var conversation = new AgentConversation(); conversation.AddUser(prompt);
        return new("http://local", "test", "Use tools", prompt, conversation, 16384, 0, tools);
    }

    private sealed class Client(Func<AiChatRequest, int, Task<AiChatCompletion>> complete) : IAiChatClient
    {
        public List<AiChatRequest> Requests { get; } = [];
        public Task<IReadOnlyList<string>> GetModelsAsync(string endpoint, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<AiChatCompletion> CompleteAsync(AiChatRequest request, CancellationToken token = default)
        { Requests.Add(request); return complete(request, Requests.Count - 1); }
    }

    private sealed class Toolset : IAgentToolset
    {
        public IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } = new[] { "inspect", "add_circle", "new_future_tool", "capture_view" }
            .Select(name => new AiToolDefinition(name, name, JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false }))).ToArray();
        public List<string> Executed { get; } = [];
        public Func<string, Task<string>> Execute { get; init; } = _ => Task.FromResult("{}");
        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) =>
            ToolDefinitions.Where(tool => tool.Name == "inspect" || (prompt.Contains("circle") && tool.Name == "add_circle")).ToArray();
        public Task<string> ExecuteAsync(AiToolCall call, CancellationToken token)
        { Executed.Add(call.Name); return Execute(call.Name); }
    }
}
