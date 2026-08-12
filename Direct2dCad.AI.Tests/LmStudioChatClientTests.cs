using System.Net;
using System.Text;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;

namespace Direct2dCad.AI.Tests;

public sealed class LmStudioChatClientTests
{
    [Fact]
    public async Task GetModelsAsync_ReturnsSortedDistinctModelIds()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"data":[{"id":"z-model"},{"id":"a-model"},{"id":"a-model"}]}"""));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        var models = await client.GetModelsAsync("http://localhost:1234/v1");

        Assert.Equal(["a-model", "z-model"], models);
        Assert.Equal("http://localhost:1234/v1/models", handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetModelsAsync_ReturnsEmptyWhenServerOmitsData()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        var models = await client.GetModelsAsync("");

        Assert.Empty(models);
        Assert.Equal(
            "http://localhost:1234/v1/models",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetModelsAsync_RejectsInvalidEndpoint()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetModelsAsync("not-an-http-url"));
    }

    [Fact]
    public async Task GetModelsAsync_PropagatesServerErrorDetails()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("model server unavailable", Encoding.UTF8, "text/plain")
        });
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetModelsAsync("http://localhost:1234/v1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("model server unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ParsesToolCallsAndSendsOpenAiToolSchema()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {
              "model":"local-model",
              "choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{
                "id":"call-1","type":"function","function":{"name":"add_line","arguments":"{\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":5}"}
              }]}}]
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { x1 = new { type = "number" } },
            required = new[] { "x1" }
        });

        var completion = await client.CompleteAsync(new AiChatRequest(
            "http://localhost:1234/v1",
            "local-model",
            [AiChatMessage.System("system"), AiChatMessage.User("draw")],
            [new AiToolDefinition("add_line", "Add a line", schema)],
            MaxOutputTokens: 777));

        var toolCall = Assert.Single(completion.ToolCalls);
        Assert.Equal("call-1", toolCall.Id);
        Assert.Equal("add_line", toolCall.Name);
        Assert.Equal("local-model", completion.Model);
        Assert.Contains("\"tool_choice\":\"auto\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"add_line\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":777", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_SendsImageContentAsOpenAiImageUrlPart()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"model":"vision-model","choices":[{"message":{"role":"assistant","content":"I see an image."}}]}"""));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await client.CompleteAsync(new AiChatRequest(
            "http://localhost:1234/v1",
            "vision-model",
            [AiChatMessage.User(
                "describe this",
                [
                    AiChatContentPart.TextPart("describe this"),
                    AiChatContentPart.Image("data:image/png;base64,AA==")
                ])],
            []));

        Assert.Contains("\"type\":\"image_url\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AA==", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_SendsTextFileAsTextContentPart()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"model":"text-model","choices":[{"message":{"role":"assistant","content":"read"}}]}"""));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await client.CompleteAsync(new AiChatRequest(
            "http://localhost:1234/v1",
            "text-model",
            [AiChatMessage.User(
                "summarize",
                [AiChatContentPart.TextPart("summarize"),
                 AiChatContentPart.FileText("notes.txt", "text/plain", "important notes")])],
            []));

        Assert.Contains("important notes", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("notes.txt", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"text\"", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ClampsTemperatureAndOutputTokens()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"choices":[{"message":{"content":"ok"}}]}"""));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await client.CompleteAsync(new AiChatRequest(
            "http://localhost:1234/v1",
            "local-model",
            [AiChatMessage.User("hello")],
            [],
            Temperature: 99,
            MaxOutputTokens: 999999));

        Assert.Contains("\"temperature\":2", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":32768", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_RejectsMissingModel()
    {
        var client = new LmStudioChatClient(new HttpClient(
            new StubHttpMessageHandler(_ => JsonResponse("{}"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                " ",
                [AiChatMessage.User("hello")],
            [])));
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnsupportedMessageRoleBeforeSending()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [new AiChatMessage((AiChatRole)99, "hello")],
                [])));

        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task CompleteAsync_RejectsImageWithoutDataUrlBeforeSending()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [AiChatMessage.User(
                    "describe",
                    [new AiChatContentPart(AiChatContentPartType.Image)])],
                [])));

        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnsupportedContentPartBeforeSending()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [AiChatMessage.User(
                    "describe",
                    [new AiChatContentPart((AiChatContentPartType)99)])],
                [])));

        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task CompleteAsync_RejectsResponseWithoutAssistantMessage()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{\"choices\":[]}"));
        var client = new LmStudioChatClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [AiChatMessage.User("hello")],
                [])));
    }

    [Fact]
    public async Task CompleteAsync_IgnoresMalformedToolCalls()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"missing-function"},
              {"function":{"name":"missing-id"}},
              {"id":"call-1","function":{"name":"valid","arguments":null}}
            ]}}]}
            """));
        var client = new LmStudioChatClient(new HttpClient(handler));

        var completion = await client.CompleteAsync(new AiChatRequest(
            "http://localhost:1234/v1",
            "local-model",
            [AiChatMessage.User("hello")],
            []));

        var call = Assert.Single(completion.ToolCalls);
        Assert.Equal("valid", call.Name);
        Assert.Equal("{}", call.ArgumentsJson);
    }

    [Fact]
    public async Task CompleteAsync_MapsNestedContextWindowError()
    {
        const string error =
            """{"error":"Engine protocol predict request returned 400: {\"error\":{\"code\":400,\"message\":\"request (12945 tokens) exceeds the available context size (8192 tokens), try increasing it\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":12945,\"n_ctx\":8192}}"}""";
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(error, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        var exception = await Assert.ThrowsAsync<AiContextWindowExceededException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [AiChatMessage.User("draw")],
                [])));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(12945, exception.PromptTokens);
        Assert.Equal(8192, exception.ContextWindowTokens);
    }

    [Fact]
    public async Task CompleteAsync_IncludesServerErrorDetails()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("model does not support tools", Encoding.UTF8, "text/plain")
        });
        using var httpClient = new HttpClient(handler);
        var client = new LmStudioChatClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(
            new AiChatRequest(
                "http://localhost:1234/v1",
                "local-model",
                [AiChatMessage.User("hello")],
                [])));

        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("model does not support tools", exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
