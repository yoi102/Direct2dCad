namespace Direct2dCad.CommandLine;

public sealed class CadCommandLineService : ICadCommandLineService
{
    private readonly CadCommandLineRegistry _registry = new();

    public CadCommandLineService(IEnumerable<ICadCommandLineHandler>? handlers = null)
    {
        RegisterBuiltInHandlers();
        if (handlers is null)
            return;

        foreach (var handler in handlers)
            _registry.Register(handler);
    }

    public IReadOnlyList<CadCommandLineDescriptor> Commands =>
        _registry.Handlers.Select(handler => handler.Descriptor).ToArray();

    public CadCommandLineResult Execute(string commandLine, ICadCommandLineContext? context)
    {
        if (context is not null && CadCommandLinePointParser.LooksLikePoint(commandLine))
            return ExecutePoint(commandLine, context);

        var tokens = CadCommandLineSyntax.Tokenize(commandLine);
        if (tokens.Length == 0)
            return Failure("Enter a command. Type HELP to list available commands.");

        if (!_registry.TryResolve(tokens[0], out var handler) || handler is null)
            return Failure($"Unknown command '{tokens[0]}'. Type HELP to list available commands.");

        if (context is null && handler.Descriptor.Name is not ("HELP" or "CLEAR"))
            return Failure("No active document.");

        return handler.Execute(new CadCommandLineRequest(
            context ?? NullCadCommandLineContext.Instance,
            tokens.Skip(1).ToArray()));
    }

    public IReadOnlyList<string> Complete(string commandPrefix, int maximumCount = 12) =>
        _registry.Complete(commandPrefix, maximumCount);

    private void RegisterBuiltInHandlers()
    {
        Register("HELP", "?", "HELP [command]", "List commands or show command help.", ExecuteHelp);
        Register("CLEAR", "CLS", "CLEAR", "Clear terminal output.", _ => new(true, string.Empty, ClearOutput: true));
        Register("STATUS", "ST", "STATUS", "Show document and interaction status.", request => Success(
            $"Document: {request.Context.DocumentName} | Entities: {request.Context.EntityCount} | " +
            $"Selected: {request.Context.SelectionCount} | Mode: {request.Context.ToolMode}"));
        Register(
            "RENDERSTATS",
            "RS",
            "RENDERSTATS",
            "Show renderer workload, retained-cache, and GPU-memory statistics.",
            ExecuteRenderStatistics);
        Register("UNDO", "U", "UNDO", "Undo the last document command.", ExecuteUndo);
        Register("REDO", "", "REDO", "Redo the last undone document command.", ExecuteRedo);
        Register("FIT", "ZE", "FIT", "Fit visible content to the viewport.", ExecuteFit);
        Register("ZOOM", "Z", "ZOOM EXTENTS", "Zoom to drawing extents.", ExecuteZoom);
        RegisterMode("SELECT", "S", "SELECT", "Enter selection mode.", CadCommandLineDrawingMode.Select);
        Register("SELECTALL", "ALL", "SELECTALL", "Select all selectable entities.", request =>
            Success($"Selected {request.Context.SelectAll()} entities."));
        Register("ERASE", "DELETE, E", "ERASE", "Delete selected entities.", ExecuteErase);
        Register(
            "COPY",
            "CO",
            "COPY",
            "Copy selected entities and dependent block definitions.",
            request => request.Context.CopySelection() is { } summary
                ? Success($"Copied {FormatClipboardSummary(summary)}.")
                : Failure("Nothing is selected or the selection contains no supported CAD content."));
        Register(
            "PASTE",
            "V",
            "PASTE",
            "Start a movable paste preview, including copied block definitions.",
            request => request.Context.BeginPaste() is { } summary
                ? Success($"Paste preview active for {FormatClipboardSummary(summary)}. Move it and click to place.")
                : Failure("The clipboard does not contain supported CAD content."));
        RegisterMode("LINE", "L", "LINE", "Enter line drawing mode.", CadCommandLineDrawingMode.Line);
        Register("CIRCLE", "C", "CIRCLE [RADIUS|DIAMETER|2P|3P]", "Enter a circle drawing mode.", request =>
            ActivateMode(request.Context, ParseCircleMode(request.Arguments)));
        Register("ARC", "A", "ARC [3P|SCE|SCA|SCL|SEA|SED|SER|CSE|CSA|CSL|CONTINUE]", "Enter an arc drawing mode.", request =>
            ActivateMode(request.Context, ParseArcMode(request.Arguments)));
        Register("ELLIPSE", "EL", "ELLIPSE [CENTER|AXIS|ARC]", "Enter an ellipse drawing mode.", request =>
            ActivateMode(request.Context, ParseEllipseMode(request.Arguments)));
        RegisterMode("RECTANGLE", "REC", "RECTANGLE", "Enter rectangle drawing mode.", CadCommandLineDrawingMode.Rectangle);
        RegisterMode("POLYLINE", "PL", "POLYLINE", "Enter polyline drawing mode.", CadCommandLineDrawingMode.Polyline);
        RegisterMode("POLYGON", "POL", "POLYGON", "Enter polygon drawing mode.", CadCommandLineDrawingMode.Polygon);
        RegisterMode("SPLINE", "SPL", "SPLINE", "Enter spline drawing mode.", CadCommandLineDrawingMode.Spline);
        RegisterMode("TEXT", "T", "TEXT", "Enter text drawing mode.", CadCommandLineDrawingMode.Text);
        RegisterMode("ORIGIN", "OR", "ORIGIN", "Enter origin placement mode.", CadCommandLineDrawingMode.SetOrigin);
        RegisterMode("MVIEW", "MV", "MVIEW", "Create and adjust a paper-space model viewport.", CadCommandLineDrawingMode.LayoutViewport);
        Register("DONE", "D", "DONE", "Complete the current multi-point drawing.", request =>
            request.Context.CompleteCurrentDrawing()
                ? Success("Current drawing completed.")
                : Failure("The current drawing cannot be completed yet."));
        Register("CANCEL", "ESC", "CANCEL", "Cancel the current interaction and select.", request =>
        {
            request.Context.Cancel();
            return Success("Current interaction cancelled. Select mode active.");
        });
    }

    private void Register(
        string name,
        string aliases,
        string syntax,
        string description,
        Func<CadCommandLineRequest, CadCommandLineResult> execute)
    {
        _registry.Register(new DelegateCommandLineHandler(
            new CadCommandLineDescriptor(name, aliases, syntax, description),
            execute));
    }

    private void RegisterMode(
        string name,
        string aliases,
        string syntax,
        string description,
        CadCommandLineDrawingMode mode)
    {
        Register(name, aliases, syntax, description, request => ActivateMode(request.Context, mode));
    }

    private CadCommandLineResult ExecuteHelp(CadCommandLineRequest request)
    {
        if (request.Arguments.Count > 0)
        {
            if (!_registry.TryResolve(request.Arguments[0], out var handler) || handler is null)
                return Failure($"Unknown command '{request.Arguments[0]}'.");

            return Success(FormatHelp(handler.Descriptor));
        }

        return Success(
            "Available commands:" + Environment.NewLine +
            string.Join(Environment.NewLine, Commands.Select(FormatHelp)));
    }

    private static CadCommandLineResult ExecuteUndo(CadCommandLineRequest request)
    {
        if (!request.Context.CanUndo)
            return Failure("Nothing to undo.");

        request.Context.Undo();
        return Success("Undo completed.");
    }

    private static CadCommandLineResult ExecuteRedo(CadCommandLineRequest request)
    {
        if (!request.Context.CanRedo)
            return Failure("Nothing to redo.");

        request.Context.Redo();
        return Success("Redo completed.");
    }

    private static CadCommandLineResult ExecuteFit(CadCommandLineRequest request)
    {
        request.Context.FitToWindow();
        return Success("View fitted to visible content.");
    }

    private static CadCommandLineResult ExecuteZoom(CadCommandLineRequest request)
    {
        if (request.Arguments.Count == 0 ||
            CadCommandLineSyntax.NormalizeCommandName(request.Arguments[0]) is "E" or "EXTENTS")
        {
            return ExecuteFit(request);
        }

        return Failure("Usage: ZOOM EXTENTS");
    }

    private static CadCommandLineResult ExecuteErase(CadCommandLineRequest request)
    {
        var count = request.Context.DeleteSelection();
        return count > 0
            ? Success($"Deleted {count} entities.")
            : Failure("Nothing is selected.");
    }

    private static CadCommandLineResult ExecuteRenderStatistics(CadCommandLineRequest request)
    {
        if (request.Context.GetRenderStatistics() is not { } statistics)
            return Failure("Render statistics are not available yet.");

        return Success(string.Join(Environment.NewLine,
            $"Frame: {statistics.FramesPerSecond:F1} FPS | avg {statistics.AverageFrameMilliseconds:F2} ms | " +
            $"last {statistics.LastRenderMilliseconds:F2} ms | " +
            $"{(statistics.IsFullFrame ? "full" : "partial")} ({statistics.DirtyRegionCount} dirty)",
            $"Scene: passes {statistics.ScenePassCount} | visible {statistics.VisibleEntityCount} | " +
            $"submissions {statistics.EntitySubmissionCount} | fallback {statistics.FallbackEntityCount}",
            $"Timing: cache {statistics.CachePreparationMilliseconds:F2} ms | " +
            $"background {statistics.BackgroundRenderMilliseconds:F2} ms | " +
            $"entities {statistics.EntityRenderMilliseconds:F2} ms | " +
            $"transient {statistics.TransientRenderMilliseconds:F2} ms | " +
            $"selection {statistics.SelectionRenderMilliseconds:F2} ms",
            $"Timing I/O: OLE prepare {statistics.OlePreparationMilliseconds:F2} ms | " +
            $"surface draw {statistics.SurfaceDrawMilliseconds:F2} ms",
            $"Blocks: refs {statistics.BlockReferenceCount} | expanded {statistics.ExpandedBlockEntityCount} | " +
            $"definition CL replay/build {statistics.BlockDefinitionCommandListReplayCount}/" +
            $"{statistics.BlockDefinitionCommandListBuildCount}",
            $"Retained: scene CL replay/build {statistics.CommandListReplayCount}/{statistics.CommandListBuildCount} | " +
            $"selection {statistics.SelectionCommandListReplayCount}/{statistics.SelectionCommandListBuildCount} | " +
            $"tiles {statistics.TileReplayCount}/{statistics.TileBuildCount}",
            $"Geometry realization: fill {statistics.GeometryRealizationFillDrawCount} | " +
            $"stroke {statistics.GeometryRealizationStrokeDrawCount} | " +
            $"build/fallback {statistics.GeometryRealizationBuildCount}/{statistics.GeometryRealizationFallbackCount}",
            $"Hatch/OLE: lines {statistics.HatchLineSubmissionCount} | " +
            $"simplified families {statistics.HatchSimplifiedLineFamilyCount} | " +
            $"OLE tile builds {statistics.OleTileBuildCount}",
            $"GPU cache: {FormatBytes(statistics.GpuCacheBytes)} current | " +
            $"{FormatBytes(statistics.GpuCachePeakBytes)} peak | " +
            $"{FormatBytes(statistics.GpuCacheBudgetBytes)} budget | " +
            $"evictions {statistics.GpuCacheEvictionCount}",
            $"  scene tiles {FormatBytes(statistics.SceneTileCacheBytes)} | " +
            $"scene CL {FormatBytes(statistics.CommandListCacheBytes)} | " +
            $"selection CL {FormatBytes(statistics.SelectionCommandListCacheBytes)} | " +
            $"block CL {FormatBytes(statistics.BlockDefinitionCacheBytes)}",
            $"  realizations {FormatBytes(statistics.GeometryRealizationCacheBytes)} | " +
            $"hatch tiles {FormatBytes(statistics.HatchTileCacheBytes)} | " +
            $"images {FormatBytes(statistics.ImageBitmapCacheBytes)} | " +
            $"OLE tiles {FormatBytes(statistics.OleTileCacheBytes)}"));
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024.0;
        const double mib = kib * 1024.0;
        const double gib = mib * 1024.0;
        return bytes >= gib
            ? $"{bytes / gib:F2} GiB"
            : bytes >= mib
                ? $"{bytes / mib:F1} MiB"
                : bytes >= kib
                    ? $"{bytes / kib:F1} KiB"
                    : $"{bytes} B";
    }

    private static CadCommandLineResult ActivateMode(
        ICadCommandLineContext context,
        CadCommandLineDrawingMode mode)
    {
        context.SetToolMode(mode);
        return Success($"{mode} mode active. Specify a point on the canvas or enter X,Y.");
    }

    private static CadCommandLineResult ExecutePoint(string commandLine, ICadCommandLineContext context)
    {
        if (context.ToolMode == CadCommandLineDrawingMode.Select)
            return Failure("Start a drawing command before entering a point.");

        if (!CadCommandLinePointParser.TryParse(
                commandLine,
                context.LastInputPoint,
                out var point,
                out var error))
        {
            return Failure(error ?? "Invalid point.");
        }

        return context.SubmitDrawingPoint(point)
            ? Success($"Point accepted: {point.X:G10},{point.Y:G10}")
            : Failure("The current tool does not accept point input.");
    }

    private static CadCommandLineDrawingMode ParseCircleMode(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return CadCommandLineDrawingMode.CircleCenterRadius;

        return CadCommandLineSyntax.NormalizeCommandName(arguments[0]) switch
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

        return CadCommandLineSyntax.NormalizeCommandName(arguments[0]) switch
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

        return CadCommandLineSyntax.NormalizeCommandName(arguments[0]) switch
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

    private static string FormatHelp(CadCommandLineDescriptor command)
    {
        var aliases = string.IsNullOrWhiteSpace(command.Aliases)
            ? string.Empty
            : $" ({command.Aliases})";
        return $"  {command.Syntax}{aliases} - {command.Description}";
    }

    private static string FormatClipboardSummary(CadCommandLineClipboardSummary summary)
    {
        var parts = new List<string>
        {
            $"{summary.EntityCount} {(summary.EntityCount == 1 ? "entity" : "entities")}"
        };
        if (summary.BlockReferenceCount > 0)
        {
            parts.Add(
                $"{summary.BlockReferenceCount} block " +
                (summary.BlockReferenceCount == 1 ? "reference" : "references"));
        }
        if (summary.BlockDefinitionCount > 0)
        {
            parts.Add(
                $"{summary.BlockDefinitionCount} dependent block " +
                (summary.BlockDefinitionCount == 1 ? "definition" : "definitions"));
        }

        return string.Join(", ", parts);
    }

    private static CadCommandLineResult Success(string message) => new(true, message);

    private static CadCommandLineResult Failure(string message) => new(false, message);

    private sealed class DelegateCommandLineHandler(
        CadCommandLineDescriptor descriptor,
        Func<CadCommandLineRequest, CadCommandLineResult> execute) : ICadCommandLineHandler
    {
        public CadCommandLineDescriptor Descriptor { get; } = descriptor;

        public CadCommandLineResult Execute(CadCommandLineRequest request) => execute(request);
    }

    private sealed class NullCadCommandLineContext : ICadCommandLineContext
    {
        public static NullCadCommandLineContext Instance { get; } = new();
        public string DocumentName => string.Empty;
        public int EntityCount => 0;
        public int SelectionCount => 0;
        public CadCommandLineDrawingMode ToolMode => CadCommandLineDrawingMode.Select;
        public bool CanUndo => false;
        public bool CanRedo => false;
        public CadCommandLinePoint? LastInputPoint => null;
        public void SetToolMode(CadCommandLineDrawingMode mode) { }
        public void Cancel() { }
        public void Undo() { }
        public void Redo() { }
        public void FitToWindow() { }
        public int SelectAll() => 0;
        public int DeleteSelection() => 0;
        public CadCommandLineClipboardSummary? CopySelection() => null;
        public CadCommandLineClipboardSummary? BeginPaste() => null;
        public bool SubmitDrawingPoint(CadCommandLinePoint point) => false;
        public bool CompleteCurrentDrawing() => false;
        public CadCommandLineRenderStatistics? GetRenderStatistics() => null;
    }
}
