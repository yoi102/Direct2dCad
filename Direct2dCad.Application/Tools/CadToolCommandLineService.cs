using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Platform;

namespace Direct2dCad.Application.Tools;

public interface ICadToolCommandLineService
{
    Task<CadToolCommandLineExecution?> TryExecuteAsync(
        string commandLine,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> Complete(string commandText, int maximumCount = 12);
    string? GetInputHint(string commandText) => null;
}

public sealed record CadToolCommandLineExecution(bool Success, string Message);

public sealed partial class CadToolCommandLineService(
    ICadToolWorkspace workspace,
    IImageImportService? imageImportService = null) : ICadToolCommandLineService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly HashSet<string> BuiltInCommandCollisions =
        new(StringComparer.OrdinalIgnoreCase) { "undo", "redo" };
    private static readonly IReadOnlyDictionary<string, AiToolDefinition> Tools =
        CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(
            tool => tool.Name,
            StringComparer.OrdinalIgnoreCase);

    public async Task<CadToolCommandLineExecution?> TryExecuteAsync(
        string commandLine,
        CancellationToken cancellationToken = default)
    {
        var (command, remainder) = SplitHead(commandLine);
        command = Direct2dCad.CommandLine.CadCommandLineSyntax.NormalizeCommandName(command);
        if (command.Length == 0)
            return null;

        var shortcut = await TryShortcutAsync(command, remainder, cancellationToken);
        if (shortcut is not null) return shortcut;

        if (command.Equals("TOOLS", StringComparison.OrdinalIgnoreCase))
            return new CadToolCommandLineExecution(true, FormatToolList(remainder));

        if (command.Equals("TOOLHELP", StringComparison.OrdinalIgnoreCase))
            return FormatToolHelpExecution(remainder);

        if (command.Equals("HELP", StringComparison.OrdinalIgnoreCase))
        {
            var (requestedTool, extra) = SplitHead(remainder);
            if (extra.Length > 0) return null;
            if (requestedTool.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase))
                return new(true, "SCRIPT \"absolute-path.scr\" — One command per line; blank lines and lines beginning with ; or # are skipped. Stops at the first error and preserves earlier edits. Nested scripts are rejected. Escape cancels.");
            if (FindShortcut(requestedTool) is { } requestedShortcut)
                return new(true, requestedShortcut.Help);
            return !BuiltInCommandCollisions.Contains(requestedTool) && Tools.ContainsKey(requestedTool)
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

        if (!Tools.TryGetValue(toolName, out var definition))
            return Failure($"Unknown CAD tool '{toolName}'. Type TOOLS to list available tools.");
        toolName = definition.Name;

        argumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();
        if (!LooksLikeJsonObject(argumentsJson))
            return Failure("Tool arguments must be one JSON object, for example: add_circle {\"center_x\":0,\"center_y\":0,\"radius\":10}");

        var executor = new CadWorkspaceToolExecutor(workspace, imageImportService);
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
        if (CompleteShortcutArguments(trimmedStart, maximumCount) is { } arguments) return arguments;
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

        return new[] { "TOOLS", "TOOL", "TOOLHELP", "CADHELP", "SCRIPT" }
            .Concat(Shortcuts.SelectMany(s => s.Names))
            .Concat(Tools.Keys.Where(name => !BuiltInCommandCollisions.Contains(name)))
            .Where(name => name.StartsWith(prefixOnly, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)
            .ToArray();
    }

    private static CadToolCommandLineExecution FormatToolHelpExecution(string input)
    {
        var (toolName, extra) = SplitHead(input);
        if (extra.Length > 0) return Failure("Usage: TOOLHELP <tool-name>.");
        if (toolName.Length == 0)
            return Failure("Usage: TOOLHELP <tool-name>.");
        if (!Tools.TryGetValue(toolName, out var tool))
            return Failure($"Unknown CAD tool '{toolName}'. Type TOOLS to list available tools.");

        var schema = Direct2dCad.AI.Contracts.CadJson.Serialize(tool.Parameters, IndentedJson);
        return new CadToolCommandLineExecution(
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
            return $"No CAD tools match '{normalizedFilter}'.";

        return $"CAD tools ({matches.Length}):{Environment.NewLine}" +
               string.Join(
                   Environment.NewLine,
                   matches.Select(tool => $"{tool.Name} - {tool.Description}")) +
               Environment.NewLine +
               "Use TOOLHELP <tool-name> for its JSON schema.";
    }

    private static CadToolCommandLineExecution FormatExecutionResult(string toolName, string result)
    {
        var content = AiToolResultContent.Parse(result);
        result = content.Text;
        try
        {
            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            var success = !root.TryGetProperty("success", out var successElement) ||
                          successElement.ValueKind != JsonValueKind.False;
            if (toolName == "save_document" && root.TryGetProperty("result", out var payload) &&
                payload.TryGetProperty("saved", out var saved) && saved.ValueKind == JsonValueKind.False)
                success = false;
            var formatted = Direct2dCad.AI.Contracts.CadJson.Serialize(root, IndentedJson);
            return new CadToolCommandLineExecution(
                success,
                $"{toolName}:{Environment.NewLine}{formatted}" +
                (content.Images.Count > 0 ? Environment.NewLine + "The terminal shows image metadata only. AI clients receive the image content." : string.Empty));
        }
        catch (JsonException)
        {
            return new CadToolCommandLineExecution(true, $"{toolName}:{Environment.NewLine}{result}");
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

    private static CadToolCommandLineExecution Failure(string message) => new(false, message);
}
