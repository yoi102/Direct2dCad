using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Commands;

public sealed class FitViewportBoundsCommand : ICadEditorCommand
{
    private readonly CadRectD _bounds;
    private readonly double _padding;
    private double? _previousZoom;
    private CadPointD _previousOffset;
    private double? _targetZoom;
    private CadPointD _targetOffset;

    public FitViewportBoundsCommand(CadRectD bounds, double padding = 32.0)
    {
        if (bounds.IsEmpty)
            throw new ArgumentException("Viewport bounds cannot be empty.", nameof(bounds));
        if (padding < 0 || !double.IsFinite(padding))
            throw new ArgumentOutOfRangeException(nameof(padding));

        _bounds = bounds;
        _padding = padding;
    }

    public string Name => "Fit View to Bounds";

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _previousZoom = context.Viewport.Zoom;
        _previousOffset = context.Viewport.Offset;
        if (_targetZoom is null)
        {
            var width = Math.Max(1.0, context.Viewport.ViewWidth - _padding * 2.0);
            var height = Math.Max(1.0, context.Viewport.ViewHeight - _padding * 2.0);
            var zoom = Math.Min(
                width / Math.Max(_bounds.Width, 1.0),
                height / Math.Max(_bounds.Height, 1.0));
            _targetZoom = zoom;
            _targetOffset = new CadPointD(
                context.Viewport.ViewWidth * 0.5 - _bounds.Center.X * zoom,
                context.Viewport.ViewHeight * 0.5 + _bounds.Center.Y * zoom);
        }

        context.Viewport.SetView(_targetZoom.Value, _targetOffset);
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
