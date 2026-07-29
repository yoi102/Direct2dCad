using Direct2dCad.Agent;
using Direct2dCad.AI;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Agents;

internal sealed class CadAgentToolset : IAgentToolset
{
    private readonly CadWorkspaceToolExecutor _executor;

    public CadAgentToolset(ICadToolWorkspace workspace)
    {
        _executor = new CadWorkspaceToolExecutor(workspace);
    }

    public IReadOnlyList<AiToolDefinition> ToolDefinitions =>
        CadWorkspaceToolExecutor.ToolDefinitions;

    public IReadOnlyList<AiToolDefinition> SelectTools(string prompt, bool aggressive = false) =>
        CadAgentToolSelector.Select(prompt, ToolDefinitions, aggressive);

    public string CreateSystemPrompt() => _executor.CreateToolUsageInstructions();

    public Task<string> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken) =>
        _executor.ExecuteAsync(toolCall, cancellationToken);
}
