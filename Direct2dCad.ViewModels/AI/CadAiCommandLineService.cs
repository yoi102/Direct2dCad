using System.Text.Json;
using Direct2dCad.AI;

namespace Direct2dCad.ViewModels.AI;

public interface ICadAiCommandLineService
{
    Task<CadAiCommandLineExecution?> TryExecuteAsync(
        string commandLine,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> Complete(string commandText, int maximumCount = 12);
}

public sealed record CadAiCommandLineExecution(bool Success, string Message);

internal sealed class CadAiCommandLineService(ICadAiWorkspaceService workspace) : ICadAiCommandLineService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly HashSet<string> BuiltInCommandCollisions =
        new(StringComparer.OrdinalIgnoreCase) { "undo", "redo" };
    private static readonly IReadOnlyDictionary<string, AiToolDefinition> Tools =
        CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(
            tool => tool.Name,
            StringComparer.OrdinalIgnoreCase);

    public async Task<CadAiCommandLineExecution?> TryExecuteAsync(
        string commandLine,
        CancellationToken cancellationToken = default)
    {
        var (command, remainder) = SplitHead(commandLine);
        if (command.Length == 0)
            return null;

        if (command.Equals("TOOLS", StringComparison.OrdinalIgnoreCase))
            return new CadAiCommandLineExecution(true, FormatToolList(remainder));

        if (command.Equals("TOOLHELP", StringComparison.OrdinalIgnoreCase))
            return FormatToolHelpExecution(remainder);

        if (command.Equals("HELP", StringComparison.OrdinalIgnoreCase))
        {
            var (requestedTool, _) = SplitHead(remainder);
            return Tools.ContainsKey(requestedTool)
                ? FormatToolHelpExecution(requestedTool)
                : null;
        }

        string toolName;
        string argumentsJson;
        if (command.Equals("TOOL", StringComparison.OrdinalIgnoreCase))
        {
            (toolName, argumentsJson) = SplitHead(remainder);
            if (toolName.Length == 0)
                return Failure("Usage: TOOL <tool-name> [JSON object]. Type TOOLS to list available tools.");
        }
        else
        {
            toolName = command;
            argumentsJson = remainder;
            if (BuiltInCommandCollisions.Contains(toolName) || !Tools.ContainsKey(toolName))
                return null;
        }

        if (!Tools.ContainsKey(toolName))
            return Failure($"Unknown AI CAD tool '{toolName}'. Type TOOLS to list available tools.");

        argumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();
        if (!LooksLikeJsonObject(argumentsJson))
            return Failure("Tool arguments must be one JSON object, for example: add_circle {\"center_x\":0,\"center_y\":0,\"radius\":10}");

        var executor = new CadAiWorkspaceToolExecutor(workspace);
        var result = await executor.ExecuteAsync(
            new AiToolCall(Guid.NewGuid().ToString("N"), toolName, argumentsJson),
            cancellationToken);
        return FormatExecutionResult(toolName, result);
    }

    public IReadOnlyList<string> Complete(string commandText, int maximumCount = 12)
    {
        if (maximumCount <= 0)
            return [];

        var trimmedStart = commandText.TrimStart();
        if (trimmedStart.StartsWith("TOOL ", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = trimmedStart[5..].Trim();
            if (prefix.Any(char.IsWhiteSpace))
                return [];
            return Tools.Keys
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(maximumCount)
                .Select(name => $"TOOL {name}")
                .ToArray();
        }

        var prefixOnly = commandText.Trim();
        if (prefixOnly.Any(char.IsWhiteSpace))
            return [];

        return new[] { "TOOLS", "TOOL", "TOOLHELP" }
            .Concat(Tools.Keys.Where(name => !BuiltInCommandCollisions.Contains(name)))
            .Where(name => name.StartsWith(prefixOnly, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)
            .ToArray();
    }

    private static CadAiCommandLineExecution FormatToolHelpExecution(string input)
    {
        var (toolName, _) = SplitHead(input);
        if (toolName.Length == 0)
            return Failure("Usage: TOOLHELP <tool-name>.");
        if (!Tools.TryGetValue(toolName, out var tool))
            return Failure($"Unknown AI CAD tool '{toolName}'. Type TOOLS to list available tools.");

        var schema = JsonSerializer.Serialize(tool.Parameters, IndentedJson);
        return new CadAiCommandLineExecution(
            true,
            $"{tool.Name}{Environment.NewLine}" +
            $"{tool.Description}{Environment.NewLine}" +
            $"Usage: TOOL {tool.Name} <JSON object>{Environment.NewLine}" +
            $"Parameters:{Environment.NewLine}{schema}");
    }

    private static string FormatToolList(string filter)
    {
        var normalizedFilter = filter.Trim();
        var matches = Tools.Values
            .Where(tool =>
                normalizedFilter.Length == 0 ||
                tool.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                tool.Description.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
            return $"No AI CAD tools match '{normalizedFilter}'.";

        return $"AI CAD tools ({matches.Length}):{Environment.NewLine}" +
               string.Join(
                   Environment.NewLine,
                   matches.Select(tool => $"{tool.Name} - {tool.Description}")) +
               Environment.NewLine +
               "Use TOOLHELP <tool-name> for its JSON schema.";
    }

    private static CadAiCommandLineExecution FormatExecutionResult(string toolName, string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            var success = !root.TryGetProperty("success", out var successElement) ||
                          successElement.ValueKind != JsonValueKind.False;
            var formatted = JsonSerializer.Serialize(root, IndentedJson);
            return new CadAiCommandLineExecution(
                success,
                $"{toolName}:{Environment.NewLine}{formatted}");
        }
        catch (JsonException)
        {
            return new CadAiCommandLineExecution(true, $"{toolName}:{Environment.NewLine}{result}");
        }
    }

    private static bool LooksLikeJsonObject(string value) =>
        value.Length >= 2 && value[0] == '{' && value[^1] == '}';

    private static (string Head, string Remainder) SplitHead(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return (string.Empty, string.Empty);

        var separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0
            ? (trimmed, string.Empty)
            : (trimmed[..separator], trimmed[(separator + 1)..].TrimStart());
    }

    private static CadAiCommandLineExecution Failure(string message) => new(false, message);
}
