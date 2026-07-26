using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Direct2dCad.AI;

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

        var payload = new ChatCompletionRequestPayload(
            request.Model,
            request.Messages.Select(CreateMessagePayload).ToArray(),
            request.Tools.Count == 0
                ? null
                : request.Tools.Select(CreateToolPayload).ToArray(),
            request.Tools.Count == 0 ? null : "auto",
            Math.Clamp(request.Temperature, 0, 2),
            Stream: false);

        using var response = await httpClient.PostAsJsonAsync(
            BuildEndpoint(request.Endpoint, "chat/completions"),
            payload,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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

    private static ChatMessagePayload CreateMessagePayload(AiChatMessage message)
    {
        return new ChatMessagePayload(
            Role: message.Role switch
            {
                AiChatRole.System => "system",
                AiChatRole.User => "user",
                AiChatRole.Assistant => "assistant",
                AiChatRole.Tool => "tool",
                _ => throw new ArgumentOutOfRangeException(nameof(message))
            },
            Content: message.Content,
            ToolCalls: message.ToolCalls?.Select(call => new ToolCallPayload(
                call.Id,
                "function",
                new ToolCallFunctionPayload(call.Name, call.ArgumentsJson))).ToArray(),
            ToolCallId: message.ToolCallId);
    }

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
            if (!call.TryGetProperty("function", out var function))
                continue;

            var id = call.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var name = function.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var arguments = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                result.Add(new AiToolCall(id, name, arguments ?? "{}"));
        }

        return result;
    }

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
        if (detail.Length > 800)
            detail = detail[..800];
        throw new HttpRequestException(
            $"LM Studio returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim(),
            null,
            response.StatusCode);
    }

    private sealed record ChatCompletionRequestPayload(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessagePayload> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<ToolDefinitionPayload>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ChatMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ToolCallPayload>? ToolCalls,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId);

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
