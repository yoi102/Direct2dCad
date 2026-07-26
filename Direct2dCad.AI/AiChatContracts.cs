using System.Text.Json;

namespace Direct2dCad.AI;

public enum AiChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record AiChatMessage(
    AiChatRole Role,
    string? Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static AiChatMessage System(string content) => new(AiChatRole.System, content);
    public static AiChatMessage User(string content) => new(AiChatRole.User, content);
    public static AiChatMessage Assistant(string? content, IReadOnlyList<AiToolCall>? toolCalls = null) =>
        new(AiChatRole.Assistant, content, toolCalls);
    public static AiChatMessage Tool(string toolCallId, string content) =>
        new(AiChatRole.Tool, content, ToolCallId: toolCallId);
}

public sealed record AiToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public sealed record AiChatRequest(
    string Endpoint,
    string Model,
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools,
    double Temperature = 0.2);

public sealed record AiChatCompletion(
    string? Content,
    IReadOnlyList<AiToolCall> ToolCalls,
    string? Model);

public interface IAiChatClient
{
    Task<IReadOnlyList<string>> GetModelsAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task<AiChatCompletion> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAiAssistantSettingsStore
{
    AiAssistantSettings Load();
    void Save(AiAssistantSettings settings);
}
