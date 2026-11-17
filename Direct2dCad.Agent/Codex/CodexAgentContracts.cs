using Direct2dCad.AI.Contracts;

// Contract lives with the agent runtime so presentation code can depend on it without
// referencing the Codex app-server implementation (Direct2dCad.Agent.Codex).
namespace Direct2dCad.Agent.Codex;

public sealed record CodexAgentOptions(
    string ExecutablePath,
    string Model,
    string ReasoningEffort,
    string ServiceTier,
    string WorkingDirectory,
    int ContextWindowTokens = AiAssistantSettings.DefaultContextWindowTokens);

public sealed record CodexAgentRunRequest(
    string Prompt,
    string WorkspaceContext,
    CodexAgentOptions Options,
    IAgentToolset? Toolset,
    IReadOnlyList<AiChatContentPart>? ContentParts = null);

public sealed record CodexAgentRunResult(
    string? Model,
    bool ResponseWasEmpty);

public interface ICodexAgentClient
{
    Task<IReadOnlyList<string>> GetModelsAsync(
        CodexAgentOptions options,
        CancellationToken cancellationToken = default);

    Task<CodexAgentRunResult> RunAsync(
        CodexAgentRunRequest request,
        Func<AgentRunEvent, ValueTask>? reportEvent = null,
        CancellationToken cancellationToken = default);

    Task ResetConversationAsync(CancellationToken cancellationToken = default);
}
