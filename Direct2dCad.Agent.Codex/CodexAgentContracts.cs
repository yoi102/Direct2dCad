using Direct2dCad.Agent;

namespace Direct2dCad.Agent.Codex;

public sealed record CodexAgentOptions(
    string ExecutablePath,
    string Model,
    string ReasoningEffort,
    string ServiceTier,
    string WorkingDirectory);

public sealed record CodexAgentRunRequest(
    string Prompt,
    string WorkspaceContext,
    CodexAgentOptions Options,
    IAgentToolset? Toolset);

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
