using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent;

public sealed class AgentRunner(IAiChatClient chatClient) : IAgentRunner
{
    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        Func<AgentRunEvent, ValueTask>? reportEvent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        IReadOnlyList<AiToolDefinition> selectedTools = SelectInitialTools(request);
        var contextWindowTokens = NormalizeContextWindow(request.ContextWindowTokens);

        for (var round = 0; round < request.MaximumToolRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = await CompleteWithContextRetryAsync(
                request,
                selectedTools,
                contextWindowTokens,
                reportEvent,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            contextWindowTokens = completion.ContextWindowTokens;

            request.Conversation.AddAssistant(completion.Completion);
            if (!string.IsNullOrWhiteSpace(completion.Completion.Content))
            {
                await ReportAsync(
                    reportEvent,
                    new AgentRunEvent(
                        AgentRunEventKind.AssistantMessage,
                        completion.Completion.Content.Trim()));
            }

            if (completion.Completion.ToolCalls.Count == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new AgentRunResult(
                    completion.Completion.Model,
                    contextWindowTokens,
                    string.IsNullOrWhiteSpace(completion.Completion.Content));
            }

            if (request.Toolset is null)
                throw new InvalidOperationException("The model requested tools, but no agent toolset is available.");

            var images = new List<(string ToolName, IReadOnlyList<AiChatContentPart> Parts)>();
            foreach (var toolCall in completion.Completion.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string result;
                if (toolCall.Name == AgentToolDiscovery.Name)
                {
                    var discovered = AgentToolDiscovery.Execute(request.Toolset, toolCall.ArgumentsJson);
                    result = discovered.Result;
                    selectedTools = new[] { AgentToolDiscovery.Definition }.Concat(discovered.Tools).Concat(selectedTools)
                        .DistinctBy(tool => tool.Name).ToArray();
                }
                else result = await request.Toolset.ExecuteAsync(toolCall, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var content = request.Conversation.AddToolResult(toolCall, result);
                if (content.Images.Count > 0) images.Add((toolCall.Name, content.Images));
                await ReportAsync(
                    reportEvent,
                    new AgentRunEvent(
                        AgentRunEventKind.ToolResult,
                        content.Text,
                        toolCall.Name));
            }
            // Finish every tool response in the assistant exchange before appending
            // image-bearing user messages; providers require contiguous tool replies.
            foreach (var item in images) request.Conversation.AddToolImages(item.ToolName, item.Parts);
        }

        throw new InvalidOperationException("The model exceeded the maximum number of agent tool rounds.");
    }

    private async Task<CompletionAttempt> CompleteWithContextRetryAsync(
        AgentRunRequest request,
        IReadOnlyList<AiToolDefinition> selectedTools,
        int contextWindowTokens,
        Func<AgentRunEvent, ValueTask>? reportEvent,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var aggressive = attempt > 0;
            var context = AgentRequestContextBuilder.Build(
                request.SystemPrompt,
                request.Conversation.Messages,
                selectedTools,
                contextWindowTokens,
                aggressive);
            try
            {
                var completion = await chatClient.CompleteAsync(
                    new AiChatRequest(
                        request.Endpoint,
                        request.Model,
                        context.Messages,
                        context.Tools,
                        request.Temperature,
                        context.MaxOutputTokens),
                    cancellationToken);
                return new CompletionAttempt(completion, contextWindowTokens);
            }
            catch (AiContextWindowExceededException exception) when (attempt == 0)
            {
                if (exception.ContextWindowTokens is { } reportedContext)
                    contextWindowTokens = NormalizeContextWindow(reportedContext);

                await ReportAsync(
                    reportEvent,
                    new AgentRunEvent(
                        AgentRunEventKind.ContextReduced,
                        ContextWindowTokens: contextWindowTokens));
            }
        }

        throw new InvalidOperationException("The agent request could not fit in the configured context window.");
    }

    private static IReadOnlyList<AiToolDefinition> SelectInitialTools(AgentRunRequest request)
    {
        if (request.Toolset is null) return [];
        var selected = request.Toolset.SelectTools(request.UserPrompt).AsEnumerable();
        var prompt = request.UserPrompt.Trim();
        var continuation = new[] { "继续", "接着", "然后", "同样", "刚才", "上一个", "这个", "那个", "它" }
            .Any(term => prompt.Contains(term, StringComparison.Ordinal)) ||
            System.Text.RegularExpressions.Regex.IsMatch(prompt, @"\b(continue|go\s+on|same|that|it)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (continuation)
        {
            var previous = request.Conversation.Messages
                .Where(message => message.Role == AiChatRole.User && !message.IsToolResultAttachment && message.Content is not null && message.Content != request.UserPrompt)
                .TakeLast(4).Reverse();
            var previousTools = previous.SelectMany(message => request.Toolset.SelectTools(message.Content!));
            var calledNames = request.Conversation.Messages.SelectMany(message => message.ToolCalls ?? []).TakeLast(12).Select(call => call.Name).ToHashSet(StringComparer.Ordinal);
            selected = request.Toolset.ToolDefinitions.Where(tool => calledNames.Contains(tool.Name)).Concat(previousTools).Concat(selected);
        }
        return new[] { AgentToolDiscovery.Definition }.Concat(selected).DistinctBy(tool => tool.Name).ToArray();
    }

    private static void Validate(AgentRunRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserPrompt);
        ArgumentNullException.ThrowIfNull(request.Conversation);
        if (request.MaximumToolRounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumToolRounds));
    }

    private static int NormalizeContextWindow(int value) =>
        Math.Clamp(
            value,
            AiAssistantSettings.MinimumContextWindowTokens,
            AiAssistantSettings.MaximumContextWindowTokens);

    private static ValueTask ReportAsync(
        Func<AgentRunEvent, ValueTask>? reportEvent,
        AgentRunEvent agentEvent) =>
        reportEvent?.Invoke(agentEvent) ?? ValueTask.CompletedTask;

    private sealed record CompletionAttempt(
        AiChatCompletion Completion,
        int ContextWindowTokens);
}
