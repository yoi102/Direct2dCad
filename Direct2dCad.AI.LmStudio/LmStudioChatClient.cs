using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.AI.LmStudio;

public sealed class LmStudioChatClient(HttpClient httpClient) : IAiChatClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<string>> GetModelsAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            BuildEndpoint(endpoint, "models"),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AiChatCompletion> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidOperationException("Select an LM Studio model before sending a message.");

        var streaming = request.OnContentDelta is not null;
        var payload = new ChatCompletionRequestPayload(
            request.Model,
            request.Messages.Select(CreateMessagePayload).ToArray(),
            request.Tools.Count == 0
                ? null
                : request.Tools.Select(CreateToolPayload).ToArray(),
            request.Tools.Count == 0 ? null : "auto",
            Math.Clamp(request.Temperature, 0, 2),
            Math.Clamp(request.MaxOutputTokens, 1, 32768),
            Stream: streaming);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(request.Endpoint, "chat/completions"))
        {
            Content = new StringContent(CadJson.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };
        // Streaming must not buffer the whole body, otherwise the first token only shows up at the end.
        using var response = await httpClient.SendAsync(
            httpRequest,
            streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (streaming && string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "text/event-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return await ReadEventStreamAsync(stream, request.OnContentDelta!, response.StatusCode, cancellationToken)
                .ConfigureAwait(false);
        }

        // Servers that ignore "stream" answer with a single JSON document.
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message))
        {
            throw new InvalidOperationException("LM Studio returned a response without an assistant message.");
        }

        var content = message.TryGetProperty("content", out var contentElement) &&
                      contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString()
            : null;
        var toolCalls = ParseToolCalls(message);
        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString()
            : null;
        return new AiChatCompletion(content, toolCalls, model);
    }

    private static async Task<AiChatCompletion> ReadEventStreamAsync(
        Stream stream,
        Func<string, ValueTask> onContentDelta,
        System.Net.HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var accumulator = new StreamAccumulator();
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                // A blank line ends one server-sent event.
                if (data.Length > 0 && await HandleEventAsync(data.ToString()).ConfigureAwait(false))
                    return accumulator.ToCompletion();
                data.Clear();
                continue;
            }
            if (line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var value = line.AsSpan(5);
            if (value.Length > 0 && value[0] == ' ')
                value = value[1..];
            if (data.Length > 0)
                data.Append('\n');
            data.Append(value);
        }

        // Tolerate a stream that closes without "[DONE]" or without a final blank line.
        if (data.Length > 0)
            await HandleEventAsync(data.ToString()).ConfigureAwait(false);
        return accumulator.ToCompletion();

        async ValueTask<bool> HandleEventAsync(string eventData)
        {
            if (eventData.Trim() == "[DONE]")
                return true;

            JsonDocument document;
            try { document = JsonDocument.Parse(eventData); }
            catch (JsonException) { return false; } // Ignore keep-alives or vendor noise that is not JSON.
            using var _ = document;
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("error", out var error))
            {
                var detail = error.ValueKind == JsonValueKind.Object && ReadString(error, "message") is { } message
                    ? message
                    : error.ToString();
                if (IsContextWindowExceeded(eventData))
                {
                    throw new AiContextWindowExceededException(
                        $"LM Studio context window is too small. {detail}".Trim(),
                        statusCode,
                        ReadTokenCount(eventData, "n_prompt_tokens"),
                        ReadTokenCount(eventData, "n_ctx"));
                }
                if (detail.Length > 800)
                    detail = detail[..800];
                throw new HttpRequestException($"LM Studio stopped the response with an error. {detail}".Trim());
            }

            accumulator.Model ??= ReadString(root, "model");
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            if (!choices[0].TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                return false;

            if (ReadString(delta, "content") is { Length: > 0 } text)
            {
                accumulator.Content.Append(text);
                await onContentDelta(text).ConfigureAwait(false);
            }
            if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                var position = 0;
                foreach (var call in toolCalls.EnumerateArray())
                {
                    if (call.ValueKind == JsonValueKind.Object)
                        accumulator.AddToolCallDelta(call, position);
                    position++;
                }
            }
            return false;
        }
    }

    private sealed class StreamAccumulator
    {
        private readonly SortedDictionary<int, StreamedToolCall> _toolCalls = [];

        public StringBuilder Content { get; } = new();
        public string? Model { get; set; }

        public void AddToolCallDelta(JsonElement call, int position)
        {
            // OpenAI streams each call's id and name once, then its arguments in fragments under one index.
            var index = call.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var value)
                ? value
                : position;
            if (!_toolCalls.TryGetValue(index, out var target))
                _toolCalls[index] = target = new StreamedToolCall();
            if (ReadString(call, "id") is { Length: > 0 } id)
                target.Id = id;
            if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                return;
            if (ReadString(function, "name") is { Length: > 0 } name)
                target.Name = name;
            if (function.TryGetProperty("arguments", out var arguments))
            {
                if (arguments.ValueKind == JsonValueKind.String)
                    target.Arguments.Append(arguments.GetString());
                else if (arguments.ValueKind == JsonValueKind.Object)
                    target.Arguments.Append(arguments.GetRawText());
            }
        }

        public AiChatCompletion ToCompletion()
        {
            var calls = new List<AiToolCall>();
            foreach (var call in _toolCalls.Values)
            {
                if (string.IsNullOrWhiteSpace(call.Name))
                    continue;
                var id = string.IsNullOrWhiteSpace(call.Id) ? $"call_{calls.Count}_{Guid.NewGuid():N}" : call.Id;
                var arguments = call.Arguments.ToString();
                calls.Add(new AiToolCall(id, call.Name, string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments));
            }
            return new AiChatCompletion(Content.Length == 0 ? null : Content.ToString(), calls, Model);
        }
    }

    private sealed class StreamedToolCall
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }

    private static ChatMessagePayload CreateMessagePayload(AiChatMessage message)
    {
        message = message.CanonicalizeContent();
        return new ChatMessagePayload(
            Role: message.Role switch
            {
                AiChatRole.System => "system",
                AiChatRole.User => "user",
                AiChatRole.Assistant => "assistant",
                AiChatRole.Tool => "tool",
                _ => throw new ArgumentOutOfRangeException(nameof(message))
            },
            Content: message.ContentParts is { Count: > 0 } contentParts
                ? contentParts.Select(CreateContentPartPayload).ToArray()
                : message.Content,
            ToolCalls: message.ToolCalls?.Select(call => new ToolCallPayload(
                call.Id,
                "function",
                new ToolCallFunctionPayload(call.Name, call.ArgumentsJson))).ToArray(),
            ToolCallId: message.ToolCallId);
    }

    private static MessageContentPartPayload CreateContentPartPayload(AiChatContentPart part) =>
        part.Type switch
        {
            AiChatContentPartType.Text => new MessageContentPartPayload(
                "text",
                part.Text,
                null),
            AiChatContentPartType.Image => new MessageContentPartPayload(
                "image_url",
                null,
                new ImageUrlPayload(part.DataUrl ?? throw new ArgumentException("Image content requires a data URL."))),
            _ => throw new ArgumentOutOfRangeException(nameof(part))
        };

    private static ToolDefinitionPayload CreateToolPayload(AiToolDefinition definition) =>
        new("function", new ToolFunctionPayload(
            definition.Name,
            definition.Description,
            definition.Parameters));

    private static IReadOnlyList<AiToolCall> ParseToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<AiToolCall>();
        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                continue;

            var name = ReadString(function, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            // OpenAI sends arguments as a JSON string; some compatible servers send the object itself.
            var arguments = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.ValueKind switch
                {
                    JsonValueKind.String => argumentsElement.GetString(),
                    JsonValueKind.Object => argumentsElement.GetRawText(),
                    _ => null
                }
                : null;
            // A missing id must not silently drop the call: the tool reply only needs to echo it back.
            var id = ReadString(call, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = $"call_{result.Count}_{Guid.NewGuid():N}";
            result.Add(new AiToolCall(id, name, string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments));
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Uri BuildEndpoint(string endpoint, string relativePath)
    {
        var normalized = string.IsNullOrWhiteSpace(endpoint)
            ? AiAssistantSettings.DefaultEndpoint
            : endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate($"{normalized}/{relativePath}", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("LM Studio endpoint must be an absolute HTTP or HTTPS URL.", nameof(endpoint));
        }

        return uri;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (IsContextWindowExceeded(detail))
        {
            throw new AiContextWindowExceededException(
                $"LM Studio context window is too small. {detail}".Trim(),
                response.StatusCode,
                ReadTokenCount(detail, "n_prompt_tokens"),
                ReadTokenCount(detail, "n_ctx"));
        }
        if (detail.Length > 800)
            detail = detail[..800];
        throw new HttpRequestException(
            $"LM Studio returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim(),
            null,
            response.StatusCode);
    }

    private static bool IsContextWindowExceeded(string detail) =>
        detail.Contains("exceed_context_size_error", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);

    private static int? ReadTokenCount(string detail, string propertyName)
    {
        var match = Regex.Match(
            detail,
            $"{Regex.Escape(propertyName)}[^0-9]{{0,16}}(?<value>\\d+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value)
            ? value
            : null;
    }

    private sealed record ChatCompletionRequestPayload(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessagePayload> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<ToolDefinitionPayload>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ChatMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ToolCallPayload>? ToolCalls,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId);

    private sealed record MessageContentPartPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("image_url")] ImageUrlPayload? ImageUrl);

    private sealed record ImageUrlPayload(
        [property: JsonPropertyName("url")] string Url);

    private sealed record ToolDefinitionPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ToolFunctionPayload Function);

    private sealed record ToolFunctionPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    private sealed record ToolCallPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ToolCallFunctionPayload Function);

    private sealed record ToolCallFunctionPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);
}
