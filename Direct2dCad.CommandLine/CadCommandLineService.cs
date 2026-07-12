namespace Direct2dCad.CommandLine;

public sealed class CadCommandLineService : ICadCommandLineService
{
    private static readonly IReadOnlyList<CadCommandLineDescriptor> CommandDescriptors =
    [
        new("HELP", "?", "HELP [command]", "List commands or show command help."),
        new("CLEAR", "CLS", "CLEAR", "Clear terminal output."),
        new("STATUS", "ST", "STATUS", "Show document and interaction status."),
        new("UNDO", "U", "UNDO", "Undo the last document command."),
        new("REDO", "", "REDO", "Redo the last undone document command."),
        new("FIT", "ZE", "FIT", "Fit visible content to the viewport."),
        new("ZOOM", "Z", "ZOOM EXTENTS", "Zoom to drawing extents."),
        new("SELECT", "S", "SELECT", "Enter selection mode."),
        new("SELECTALL", "ALL", "SELECTALL", "Select all selectable entities."),
        new("ERASE", "DELETE, E", "ERASE", "Delete selected entities."),
        new("COPY", "CO", "COPY", "Copy selected entities."),
        new("PASTE", "V", "PASTE", "Start a movable paste preview."),
        new("LINE", "L", "LINE", "Enter line drawing mode."),
        new("CIRCLE", "C", "CIRCLE [RADIUS|DIAMETER|2P|3P]", "Enter a circle drawing mode."),
        new("ARC", "A", "ARC [3P|SCE|SCA|SCL|SEA|SED|SER|CSE|CSA|CSL|CONTINUE]", "Enter an arc drawing mode."),
        new("ELLIPSE", "EL", "ELLIPSE [CENTER|AXIS|ARC]", "Enter an ellipse drawing mode."),
        new("RECTANGLE", "REC", "RECTANGLE", "Enter rectangle drawing mode."),
        new("POLYLINE", "PL", "POLYLINE", "Enter polyline drawing mode."),
        new("POLYGON", "POL", "POLYGON", "Enter polygon drawing mode."),
        new("SPLINE", "SPL", "SPLINE", "Enter spline drawing mode."),
        new("TEXT", "T", "TEXT", "Enter text drawing mode."),
        new("ORIGIN", "OR", "ORIGIN", "Enter origin placement mode."),
        new("CANCEL", "ESC", "CANCEL", "Cancel the current interaction and select."),
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases = BuildAliases();

    public IReadOnlyList<CadCommandLineDescriptor> Commands => CommandDescriptors;

    public CadCommandLineResult Execute(string commandLine, ICadCommandLineContext? context)
    {
        var tokens = Tokenize(commandLine);
        if (tokens.Length == 0)
            return Failure("Enter a command. Type HELP to list available commands.");

        var requestedName = NormalizeCommandName(tokens[0]);
        var commandName = Aliases.TryGetValue(requestedName, out var resolvedName)
            ? resolvedName
            : requestedName;
        var arguments = tokens.Skip(1).ToArray();

        if (commandName == "HELP")
            return ShowHelp(arguments);

        if (commandName == "CLEAR")
            return new CadCommandLineResult(true, string.Empty, ClearOutput: true);

        if (context is null)
            return Failure("No active document.");

        return commandName switch
        {
            "STATUS" => Success(
                $"Document: {context.DocumentName} | Entities: {context.EntityCount} | " +
                $"Selected: {context.SelectionCount} | Mode: {context.ToolMode}"),
            "UNDO" => ExecuteUndo(context),
            "REDO" => ExecuteRedo(context),
            "FIT" => ExecuteFit(context),
            "ZOOM" => ExecuteZoom(context, arguments),
            "SELECT" => ActivateMode(context, CadCommandLineDrawingMode.Select),
            "SELECTALL" => Success($"Selected {context.SelectAll()} entities."),
            "ERASE" => ExecuteErase(context),
            "COPY" => context.CopySelection()
                ? Success($"Copied {context.SelectionCount} entities.")
                : Failure("Nothing is selected."),
            "PASTE" => context.BeginPaste()
                ? Success("Paste preview active. Move it and click to place.")
                : Failure("The clipboard does not contain supported CAD content."),
            "LINE" => ActivateMode(context, CadCommandLineDrawingMode.Line),
            "CIRCLE" => ActivateMode(context, ParseCircleMode(arguments)),
            "ARC" => ActivateMode(context, ParseArcMode(arguments)),
            "ELLIPSE" => ActivateMode(context, ParseEllipseMode(arguments)),
            "RECTANGLE" => ActivateMode(context, CadCommandLineDrawingMode.Rectangle),
            "POLYLINE" => ActivateMode(context, CadCommandLineDrawingMode.Polyline),
            "POLYGON" => ActivateMode(context, CadCommandLineDrawingMode.Polygon),
            "SPLINE" => ActivateMode(context, CadCommandLineDrawingMode.Spline),
            "TEXT" => ActivateMode(context, CadCommandLineDrawingMode.Text),
            "ORIGIN" => ActivateMode(context, CadCommandLineDrawingMode.SetOrigin),
            "CANCEL" => ExecuteCancel(context),
            _ => Failure($"Unknown command '{tokens[0]}'. Type HELP to list available commands.")
        };
    }

    private static CadCommandLineResult ExecuteUndo(ICadCommandLineContext context)
    {
        if (!context.CanUndo)
            return Failure("Nothing to undo.");

        context.Undo();
        return Success("Undo completed.");
    }

    private static CadCommandLineResult ExecuteRedo(ICadCommandLineContext context)
    {
        if (!context.CanRedo)
            return Failure("Nothing to redo.");

        context.Redo();
        return Success("Redo completed.");
    }

    private static CadCommandLineResult ExecuteFit(ICadCommandLineContext context)
    {
        context.FitToWindow();
        return Success("View fitted to visible content.");
    }

    private static CadCommandLineResult ExecuteZoom(
        ICadCommandLineContext context,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || NormalizeCommandName(arguments[0]) is "E" or "EXTENTS")
            return ExecuteFit(context);

        return Failure("Usage: ZOOM EXTENTS");
    }

    private static CadCommandLineResult ExecuteErase(ICadCommandLineContext context)
    {
        var count = context.DeleteSelection();
        return count > 0
            ? Success($"Deleted {count} entities.")
            : Failure("Nothing is selected.");
    }

    private static CadCommandLineResult ExecuteCancel(ICadCommandLineContext context)
    {
        context.Cancel();
        return Success("Current interaction cancelled. Select mode active.");
    }

    private static CadCommandLineResult ActivateMode(
        ICadCommandLineContext context,
        CadCommandLineDrawingMode mode)
    {
        context.SetToolMode(mode);
        return Success($"{mode} mode active.");
    }

    private static CadCommandLineDrawingMode ParseCircleMode(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return CadCommandLineDrawingMode.CircleCenterRadius;

        return NormalizeCommandName(arguments[0]) switch
        {
            "D" or "DIAMETER" => CadCommandLineDrawingMode.CircleCenterDiameter,
            "2P" or "TWOPOINT" => CadCommandLineDrawingMode.CircleTwoPoint,
            "3P" or "THREEPOINT" => CadCommandLineDrawingMode.CircleThreePoint,
            _ => CadCommandLineDrawingMode.CircleCenterRadius
        };
    }

    private static CadCommandLineDrawingMode ParseEllipseMode(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return CadCommandLineDrawingMode.EllipseCenter;

        return NormalizeCommandName(arguments[0]) switch
        {
            "AXIS" or "END" => CadCommandLineDrawingMode.EllipseAxisEnd,
            "ARC" => CadCommandLineDrawingMode.EllipseArc,
            _ => CadCommandLineDrawingMode.EllipseCenter
        };
    }

    private static CadCommandLineDrawingMode ParseArcMode(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return CadCommandLineDrawingMode.ArcThreePoint;

        return NormalizeCommandName(arguments[0]) switch
        {
            "SCE" => CadCommandLineDrawingMode.ArcStartCenterEnd,
            "SCA" => CadCommandLineDrawingMode.ArcStartCenterAngle,
            "SCL" => CadCommandLineDrawingMode.ArcStartCenterLength,
            "SEA" => CadCommandLineDrawingMode.ArcStartEndAngle,
            "SED" => CadCommandLineDrawingMode.ArcStartEndDirection,
            "SER" => CadCommandLineDrawingMode.ArcStartEndRadius,
            "CSE" => CadCommandLineDrawingMode.ArcCenterStartEnd,
            "CSA" => CadCommandLineDrawingMode.ArcCenterStartAngle,
            "CSL" => CadCommandLineDrawingMode.ArcCenterStartLength,
            "CONTINUE" or "CON" => CadCommandLineDrawingMode.ArcContinue,
            _ => CadCommandLineDrawingMode.ArcThreePoint
        };
    }

    private static CadCommandLineResult ShowHelp(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0)
        {
            var name = NormalizeCommandName(arguments[0]);
            var resolvedName = Aliases.TryGetValue(name, out var alias) ? alias : name;
            var descriptor = CommandDescriptors.FirstOrDefault(command => command.Name == resolvedName);
            return descriptor is null
                ? Failure($"Unknown command '{arguments[0]}'.")
                : Success(FormatHelp(descriptor));
        }

        var lines = CommandDescriptors.Select(FormatHelp);
        return Success("Available commands:" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    private static string FormatHelp(CadCommandLineDescriptor command)
    {
        var aliases = string.IsNullOrWhiteSpace(command.Aliases)
            ? string.Empty
            : $" ({command.Aliases})";
        return $"  {command.Syntax}{aliases} - {command.Description}";
    }

    private static IReadOnlyDictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in CommandDescriptors)
        {
            aliases[command.Name] = command.Name;
            foreach (var alias in command.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                aliases[alias] = command.Name;
        }

        return aliases;
    }

    private static string[] Tokenize(string commandLine) =>
        (commandLine ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeCommandName(string value) =>
        value.Trim().TrimStart('_', '.').ToUpperInvariant();

    private static CadCommandLineResult Success(string message) => new(true, message);

    private static CadCommandLineResult Failure(string message) => new(false, message);
}
