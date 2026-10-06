using System.Net;
using System.Text.Json;
using Direct2dCad.Agent;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;

namespace Direct2dCad.AI.Tests;

public sealed class AiImageContentIntegrationTests
{
    private const string EncodedImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j9ioAAAAASUVORK5CYII=";
    private static string ImageResult => JsonSerializer.Serialize(new
    {
        success = true,
        result = new { document_id = "document-1", document_version = 4,
            image = new { mime_type = "image/png", data_base64 = EncodedImage, width = 1, height = 1 } }
    });

    [Theory]
    [InlineData(4096, 16000)]
    [InlineData(8192, 32000)]
    public async Task LongPrompt_ReachesHttpPayloadWithLeadingAndTrailingInstructions(int context, int length)
    {
        var prompt = "KEEP_START " + new string('A', length) + " DRAW_CIRCLE_AT_10_20_RADIUS_5";
        var conversation = new AgentConversation();
        conversation.AddUser(prompt, [AiChatContentPart.TextPart(prompt)]);
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await new AgentRunner(new LmStudioChatClient(http)).RunAsync(Request(conversation, prompt, context));
        var text = handler.Requests[0].GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.StartsWith("KEEP_START", text);
        Assert.EndsWith("DRAW_CIRCLE_AT_10_20_RADIUS_5", text);
        Assert.Contains("content truncated", text);
    }

    [Fact]
    public async Task ContentAndParts_TransmitPromptOnceAndKeepImage()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new LmStudioChatClient(http);
        await client.CompleteAsync(new AiChatRequest("http://localhost/v1", "test",
            [AiChatMessage.User("inspect", [AiChatContentPart.Image($"data:image/png;base64,{EncodedImage}")])], []));
        var parts = handler.Requests[0].GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Equal("inspect", parts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToolCapture_ReachesNextLmRequestAsImageWithOriginalInstructionAndSanitizedText()
    {
        using var handler = new Handler { CallCapture = true };
        using var http = new HttpClient(handler);
        var conversation = new AgentConversation();
        conversation.AddUser("verify the circle is visible");
        var events = new List<AgentRunEvent>();
        await new AgentRunner(new LmStudioChatClient(http)).RunAsync(
            Request(conversation, "verify the circle is visible", 4096) with { Toolset = new CaptureToolset() },
            e => { events.Add(e); return ValueTask.CompletedTask; });
        var messages = handler.Requests[1].GetProperty("messages").EnumerateArray().ToArray();
        Assert.Contains(messages, message => message.GetProperty("role").GetString() == "user" &&
            message.GetProperty("content").ValueKind == JsonValueKind.String &&
            message.GetProperty("content").GetString() == "verify the circle is visible");
        var tool = Assert.Single(messages, message => message.GetProperty("role").GetString() == "tool");
        Assert.Contains("document_version", tool.GetProperty("content").GetString());
        Assert.DoesNotContain("data_base64", tool.GetProperty("content").GetString());
        var image = messages.SelectMany(message => message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray().ToArray() : [])
            .Single(part => part.GetProperty("type").GetString() == "image_url");
        Assert.Equal($"data:image/png;base64,{EncodedImage}", image.GetProperty("image_url").GetProperty("url").GetString());
        Assert.DoesNotContain(events, e => e.Content?.Contains(EncodedImage) == true);
    }

    [Fact]
    public async Task ExcessImages_FailExplicitlyBeforeSendingAnIncompleteRequest()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var conversation = new AgentConversation();
        conversation.AddUser("compare images", Enumerable.Range(0, 4).Select(_ =>
            AiChatContentPart.Image($"data:image/png;base64,{EncodedImage}")).ToArray());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AgentRunner(new LmStudioChatClient(http)).RunAsync(Request(conversation, "compare images", 4096)));
        Assert.Contains("image input cannot fit", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("text/plain", "aGVsbG8=", 1)]
    [InlineData("image/png", "broken-base64", 1)]
    [InlineData("image/png", "aGVsbG8=", 1025)]
    public void InvalidToolImage_RemovesBinaryTextAndReportsFailure(string mime, string encoded, int width)
    {
        var result = AiToolResultContent.Parse(JsonSerializer.Serialize(new
        {
            success = true, result = new { image = new { mime_type = mime, data_base64 = encoded, width, height = 1 } }
        }));
        using var document = JsonDocument.Parse(result.Text);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("data_base64", result.Text);
        Assert.Empty(result.Images);
    }

    private static AgentRunRequest Request(AgentConversation conversation, string prompt, int context) =>
        new("http://localhost/v1", "test", "Use tools to inspect CAD.", prompt, conversation, context, 0.2);

    private sealed class CaptureToolset : IAgentToolset
    {
        public IReadOnlyList<AiToolDefinition> ToolDefinitions => [new("capture_view", "Capture CAD view", JsonSerializer.SerializeToElement(new { type = "object" }))];
        public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) => ToolDefinitions;
        public Task<string> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken) => Task.FromResult(ImageResult);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool CallCapture;
        public List<JsonElement> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(document.RootElement.Clone());
            var response = CallCapture && Requests.Count == 1
                ? """{"choices":[{"message":{"content":null,"tool_calls":[{"id":"capture-1","function":{"name":"capture_view","arguments":"{}"}}]}}]}"""
                : """{"choices":[{"message":{"content":"done"}}],"model":"test"}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
