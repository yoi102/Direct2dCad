using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadToolCommandLineService
{
    private async Task<CadToolCommandLineExecution?> TryFriendlyShortcutAsync(
        string name, string[] arguments, string help, CancellationToken token)
    {
        if (name is not ("NEW" or "OPEN" or "SAVE" or "LAYER" or "DIST" or "AREA")) return null;
        token.ThrowIfCancellationRequested();
        if (name == "NEW")
        {
            if (arguments.Length > 1 || arguments.Any(string.IsNullOrWhiteSpace)) return Failure(help);
            return await ExecuteShortcutToolAsync("create_document", arguments.Length == 0
                ? new Dictionary<string, object>() : new() { ["name"] = arguments[0] }, token);
        }
        if (name == "OPEN")
        {
            if (arguments.Length != 1 || !Path.IsPathFullyQualified(arguments[0])) return Failure(help);
            return await ExecuteShortcutToolAsync("open_document", new() { ["file_path"] = arguments[0] }, token);
        }
        var document = workspace.GetActiveDocument() ?? throw new InvalidOperationException("No active document.");
        var payload = new Dictionary<string, object> { ["document_id"] = document.DocumentId };
        if (name == "SAVE")
        {
            if (arguments.Length > 1 || arguments.Length == 1 && !Path.IsPathFullyQualified(arguments[0])) return Failure(help);
            if (arguments.Length == 1) payload["file_path"] = arguments[0];
            return await ExecuteShortcutToolAsync("save_document", payload, token);
        }
        if (name == "LAYER")
        {
            var operation = arguments.Length == 0 ? "LIST" : CadCommandLineSyntax.NormalizeCommandName(arguments[0]);
            var count = operation == "LIST" ? (arguments.Length == 0 ? 0 : 1) : operation == "RENAME" ? 3 : 2;
            if (arguments.Length != count || arguments.Any(string.IsNullOrWhiteSpace)) return Failure(help);
            var tool = operation switch
            {
                "LIST" => "list_layers", "NEW" => "create_layer", "SET" => "set_drawing_layer",
                "RENAME" => "rename_layer", "ON" or "OFF" or "LOCK" or "UNLOCK" or "FREEZE" or "THAW" => "set_layer_properties",
                _ => null
            };
            if (tool is null) return Failure(help);
            if (operation == "NEW") payload["name"] = arguments[1];
            else if (operation != "LIST") payload["layer"] = arguments[1];
            if (operation == "RENAME") payload["new_name"] = arguments[2];
            if (operation is "ON" or "OFF") payload["visible"] = operation == "ON";
            if (operation is "LOCK" or "UNLOCK") payload["locked"] = operation == "LOCK";
            if (operation is "FREEZE" or "THAW") payload["frozen"] = operation == "FREEZE";
            return await ExecuteShortcutToolAsync(tool, payload, token);
        }
        var unit = document.Session.CadEditor.Document.DocumentSettings.Unit;
        if (name == "DIST")
        {
            if (arguments.Length != 2) return Failure(help);
            var points = arguments.Select(argument => CadCommandLinePointParser.TryParse(argument, null, unit, out var point, out var error)
                ? new { x = point.X, y = point.Y } : throw new ArgumentException(error)).ToArray();
            payload["points"] = points;
        }
        else
        {
            var ids = arguments.Length == 0 ? document.Session.CadEditor.Selection.EntityIds.Select(id => id.Value).ToArray()
                : arguments.Select(argument => long.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                    ? id : throw new ArgumentException("AREA requires positive entity IDs.")).ToArray();
            if (ids.Length == 0) return Failure("Select closed entities or enter AREA entity_id [...].");
            payload["entity_ids"] = ids;
        }
        var executor = new CadWorkspaceToolExecutor(workspace, imageImportService);
        var raw = await executor.ExecuteAsync(new AiToolCall(Guid.NewGuid().ToString("N"), "measure_geometry", Direct2dCad.AI.Contracts.CadJson.Serialize(payload)), token);
        var result = FormatExecutionResult("measure_geometry", raw);
        if (!result.Success) return result;
        using var json = JsonDocument.Parse(raw);
        var data = json.RootElement.GetProperty("result");
        var symbol = CadUnitConversion.GetSymbol(unit);
        var scale = CadUnitConversion.FromMillimeters(1, unit);
        if (name == "DIST")
        {
            var measured = data.GetProperty("points")[0];
            var distance = measured.GetProperty("total_distance_millimeters").GetDouble() * scale;
            var angle = measured.GetProperty("segments")[0].GetProperty("angle_degrees").GetDouble();
            return new(true, FormattableString.Invariant($"Distance: {distance:G10} {symbol} | Angle: {angle:G10}°"));
        }
        var lines = new List<string>();
        foreach (var entity in data.GetProperty("entities").EnumerateArray())
        {
            if (entity.GetProperty("area_square_millimeters").ValueKind != JsonValueKind.Number)
                return Failure($"Entity {entity.GetProperty("entity_id")} is not a supported closed area.");
            var area = entity.GetProperty("area_square_millimeters").GetDouble() * scale * scale;
            var approximate = entity.GetProperty("approximate").GetBoolean() ? " (approximate)" : "";
            lines.Add(FormattableString.Invariant($"Entity {entity.GetProperty("entity_id")}: Area {area:G10} {symbol}²{approximate}"));
        }
        return new(true, string.Join(Environment.NewLine, lines));
    }

    private async Task<CadToolCommandLineExecution> ExecuteShortcutToolAsync(string tool, Dictionary<string, object> payload, CancellationToken token)
    {
        var executor = new CadWorkspaceToolExecutor(workspace, imageImportService);
        var result = await executor.ExecuteAsync(new AiToolCall(Guid.NewGuid().ToString("N"), tool, Direct2dCad.AI.Contracts.CadJson.Serialize(payload)), token);
        return FormatExecutionResult(tool, result);
    }

    public string? GetInputHint(string commandText)
    {
        var (command, remainder) = SplitHead(commandText);
        if (command.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase)) return "SCRIPT \"absolute-path.scr\" — One command per line; stop at the first error; earlier edits remain.";
        if (FindShortcut(command) is { } shortcut)
        {
            var count = CadCommandLineSyntax.TryTokenize(remainder, out var arguments, out _) ? arguments.Length : 0;
            var position = count + (commandText.Length > 0 && char.IsWhiteSpace(commandText[^1]) ? 1 : count == 0 ? 1 : 0);
            return shortcut.Help + $" [argument {position}]";
        }
        if (command.Equals("TOOL", StringComparison.OrdinalIgnoreCase)) (command, _) = SplitHead(remainder);
        return Tools.TryGetValue(command, out var tool) ? $"{tool.Name} {{JSON}} — {tool.Description}" : null;
    }

    private IReadOnlyList<string>? CompleteShortcutArguments(string text, int maximumCount)
    {
        var separator = text.IndexOfAny([' ', '\t']);
        if (separator < 0) return null;
        var command = CadCommandLineSyntax.NormalizeCommandName(text[..separator]);
        var remainder = text[(separator + 1)..].TrimStart();
        if (command is "HELP" or "TOOLHELP")
            return (command == "HELP" ? Shortcuts.Select(s => s.Names[0]).Concat(Tools.Keys) : Tools.Keys)
                .Where(value => value.StartsWith(remainder, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
                .Take(maximumCount).Select(value => command + " " + value).ToArray();
        if (command is "LAYER" or "LA")
        {
            var argumentSeparator = remainder.IndexOfAny([' ', '\t']);
            if (argumentSeparator < 0)
                return new[] { "LIST", "NEW", "SET", "ON", "OFF", "LOCK", "UNLOCK", "FREEZE", "THAW", "RENAME" }
                    .Where(value => value.StartsWith(remainder, StringComparison.OrdinalIgnoreCase)).Take(maximumCount).Select(value => "LAYER " + value).ToArray();
            var operation = CadCommandLineSyntax.NormalizeCommandName(remainder[..argumentSeparator]);
            if (operation is not ("SET" or "ON" or "OFF" or "LOCK" or "UNLOCK" or "FREEZE" or "THAW" or "RENAME")) return [];
            var prefix = remainder[(argumentSeparator + 1)..].TrimStart().Trim('"');
            return workspace.GetActiveDocument()?.Session.CadEditor.Document.Layers.Values
                .Where(layer => layer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase).Take(maximumCount)
                .Select(layer => $"LAYER {operation} {CadCommandLineSyntax.QuoteArgument(layer.Name)}").ToArray() ?? [];
        }
        if (command is "SUBTRACT" or "AREA")
        {
            var document = workspace.GetActiveDocument();
            if (document is null) return [];
            var lastSpace = remainder.LastIndexOfAny([' ', '\t']);
            var previous = lastSpace < 0 ? "" : remainder[..(lastSpace + 1)];
            var prefix = lastSpace < 0 ? remainder : remainder[(lastSpace + 1)..];
            var candidates = command == "SUBTRACT" ? document.Session.CadEditor.Selection.EntityIds
                : document.Session.CadEditor.Document.Entities.Values.Where(entity => !entity.IsErased).Select(entity => entity.Id);
            return candidates.Select(id => id.Value.ToString(CultureInfo.InvariantCulture)).Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
                .Take(maximumCount).Select(id => command + " " + previous + id).ToArray();
        }
        return null;
    }
}
