using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadToolCommandLineService
{
    private sealed record Shortcut(string[] Names, string Help);
    private static readonly Shortcut[] Shortcuts =
    [
        new(["MOVE", "M"], "MOVE dx,dy — Move selected entities. Distances use the current display unit."),
        new(["ROTATE", "RO"], "ROTATE angle px,py — Rotate selection around a pivot. Angle uses degrees; pivot uses the display unit."),
        new(["SCALE", "SC"], "SCALE factor px,py — Scale selection around a pivot. Factor must be positive; pivot uses the display unit."),
        new(["MIRROR", "MI"], "MIRROR x,y angle — Mirror selection across an axis through x,y. Angle uses degrees; x,y use the display unit."),
        new(["DUPLICATE", "DUP"], "DUPLICATE dx,dy — Copy selected entities with an offset in the display unit. COPY remains the clipboard command."),
        new(["UNION"], "UNION — Unite at least two selected closed entities into one Region."),
        new(["INTERSECT"], "INTERSECT — Intersect at least two selected closed entities into one Region."),
        new(["SUBTRACT"], "SUBTRACT subject_entity_id — Subtract all other selected operands from the explicit selected subject."),
        new(["NEW"], "NEW [\"name\"] — Create and activate a document."),
        new(["OPEN"], "OPEN \"absolute-path.d2cad\" — Open and activate a document."),
        new(["SAVE"], "SAVE [\"absolute-path.d2cad\"] — Save the active document; new documents require a path."),
        new(["LAYER", "LA"], "LAYER [LIST|NEW name|SET name|ON name|OFF name|LOCK name|UNLOCK name|FREEZE name|THAW name|RENAME name new_name] — Quote names containing spaces."),
        new(["DIST", "DI"], "DIST x1,y1 x2,y2 — Measure distance and angle in the current display unit."),
        new(["AREA"], "AREA [entity_id ...] — Measure selected or supplied closed entities in the current display unit squared.")
    ];

    private static Shortcut? FindShortcut(string command) => Shortcuts.FirstOrDefault(s =>
        s.Names.Contains(CadCommandLineSyntax.NormalizeCommandName(command), StringComparer.OrdinalIgnoreCase));

    private async Task<CadToolCommandLineExecution?> TryShortcutAsync(string command, string remainder, CancellationToken token)
    {
        if (command.Equals("CADHELP", StringComparison.OrdinalIgnoreCase))
            return remainder.Length > 0 ? Failure("Usage: CADHELP") : new(true, string.Join(Environment.NewLine, Shortcuts.Select(s => s.Help)) + Environment.NewLine +
                "SCRIPT \"absolute-path.scr\" — Execute terminal commands sequentially; stop on the first error. Earlier edits are retained." + Environment.NewLine +
                "All CAD/AI tools are available as TOOL <name> {JSON}. JSON geometry uses millimetres. Type TOOLS or TOOLHELP <name>.");
        if (FindShortcut(command) is not { } shortcut) return null;
        try
        {
            var arguments = CadCommandLineSyntax.Tokenize(remainder);
            var friendly = await TryFriendlyShortcutAsync(shortcut.Names[0], arguments, shortcut.Help, token);
            if (friendly is not null) return friendly;
            var document = workspace.GetActiveDocument() ?? throw new InvalidOperationException("No active document.");
            var name = shortcut.Names[0];
            var expected = name is "UNION" or "INTERSECT" ? 0 : name is "MOVE" or "DUPLICATE" or "SUBTRACT" ? 1 : 2;
            if (arguments.Length != expected) return Failure(shortcut.Help);
            double Number(string input) => double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value : throw new ArgumentException("Enter finite numbers using a decimal point.");
            (double X, double Y) Point(string input)
            {
                var parts = input.Split(',');
                if (parts.Length != 2) throw new ArgumentException("Enter coordinates as x,y.");
                var unit = document.Session.CadEditor.Document.DocumentSettings.Unit;
                var x = CadUnitConversion.ToMillimeters(Number(parts[0]), unit);
                var y = CadUnitConversion.ToMillimeters(Number(parts[1]), unit);
                if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Coordinates are too large.");
                return (x, y);
            }
            var tool = "transform_entities";
            object payload;
            switch (name)
            {
                case "MOVE": case "DUPLICATE":
                    var offset = Point(arguments[0]);
                    tool = name == "MOVE" ? "transform_entities" : "duplicate_entities";
                    payload = name == "MOVE" ?
                        (object)new { document_id = document.DocumentId, operation = "move", delta_x = offset.X, delta_y = offset.Y } :
                        new { document_id = document.DocumentId, delta_x = offset.X, delta_y = offset.Y };
                    break;
                case "ROTATE":
                    var rotatePivot = Point(arguments[1]);
                    payload = new { document_id = document.DocumentId, operation = "rotate", angle_degrees = Number(arguments[0]), pivot_x = rotatePivot.X, pivot_y = rotatePivot.Y };
                    break;
                case "SCALE":
                    var scalePivot = Point(arguments[1]);
                    payload = new { document_id = document.DocumentId, operation = "scale", factor = Number(arguments[0]), pivot_x = scalePivot.X, pivot_y = scalePivot.Y };
                    break;
                case "MIRROR":
                    var axis = Point(arguments[0]);
                    payload = new { document_id = document.DocumentId, operation = "mirror", axis_angle_degrees = Number(arguments[1]), axis_x = axis.X, axis_y = axis.Y };
                    break;
                default:
                    tool = "boolean_regions";
                    long? subject = name == "SUBTRACT" ? long.TryParse(arguments[0], out var id) && id > 0 ? id :
                        throw new ArgumentException("SUBTRACT requires a positive selected subject_entity_id.") : null;
                    var booleanArguments = new Dictionary<string, object> { ["document_id"] = document.DocumentId,
                        ["operation"] = name == "UNION" ? "union" : name == "INTERSECT" ? "intersection" : "difference",
                        ["entity_ids"] = document.Session.CadEditor.Selection.EntityIds.Select(id => id.Value).ToArray() };
                    if (subject is { } subjectId) booleanArguments["subject_entity_id"] = subjectId;
                    payload = booleanArguments;
                    break;
            }
            var executor = new CadWorkspaceToolExecutor(workspace, imageImportService);
            var result = await executor.ExecuteAsync(new AiToolCall(Guid.NewGuid().ToString("N"), tool, Direct2dCad.AI.Contracts.CadJson.Serialize(payload)), token);
            return FormatExecutionResult(tool, result);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(exception.Message + Environment.NewLine + shortcut.Help);
        }
    }
}
