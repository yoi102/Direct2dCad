using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Commands;

public sealed class ZoomViewportCommand : ICadEditorCommand
{
    private readonly CadPointD _screenAnchor;
    private readonly double _factor;
    private double? _previousZoom;
    private CadPointD _previousOffset;

    public string Name => "Zoom View";

    public ZoomViewportCommand(CadPointD screenAnchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            throw new ArgumentOutOfRangeException(nameof(factor));

        _screenAnchor = screenAnchor;
        _factor = factor;
    }

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _previousZoom = context.Viewport.Zoom;
        _previousOffset = context.Viewport.Offset;
        context.Viewport.ZoomAt(_screenAnchor, _factor);
        return CadEditorCommandResult.View();
    }

    public CadEditorCommandResult Undo(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_previousZoom is null)
            return CadEditorCommandResult.Empty;

        context.Viewport.SetView(_previousZoom.Value, _previousOffset);
        return CadEditorCommandResult.View();
    }
}
