using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Commands;

public sealed class FitViewportCommand : ICadEditorCommand
{
    private readonly double _padding;
    private double? _previousZoom;
    private CadPointD _previousOffset;
    private double? _targetZoom;
    private CadPointD _targetOffset;

    public string Name => "Fit View";

    public FitViewportCommand(double padding = 32.0)
    {
        if (padding < 0 || double.IsNaN(padding) || double.IsInfinity(padding))
            throw new ArgumentOutOfRangeException(nameof(padding));

        _padding = padding;
    }

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _previousZoom = context.Viewport.Zoom;
        _previousOffset = context.Viewport.Offset;

        if (_targetZoom is null)
        {
            var target = CalculateFitView(context);
            _targetZoom = target.Zoom;
            _targetOffset = target.Offset;
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

    private FitView CalculateFitView(CadEditorCommandContext context)
    {
        var viewport = context.Viewport;
        var viewWidth = Math.Max(1.0, viewport.ViewWidth);
        var viewHeight = Math.Max(1.0, viewport.ViewHeight);
        var contentBounds = GetVisibleEntityBounds(context.Document);

        if (contentBounds.IsEmpty)
        {
            var origin = context.Document.ViewSettings.Origin.Position;
            return new FitView(
                1.0,
                new CadPointD(
                    viewWidth * 0.5 - origin.X,
                    viewHeight * 0.5 - origin.Y));
        }

        var availableWidth = Math.Max(1.0, viewWidth - _padding * 2.0);
        var availableHeight = Math.Max(1.0, viewHeight - _padding * 2.0);
        var worldWidth = Math.Max(contentBounds.Width, 1.0);
        var worldHeight = Math.Max(contentBounds.Height, 1.0);
        var zoom = Math.Min(availableWidth / worldWidth, availableHeight / worldHeight);
        var center = contentBounds.Center;
        var offset = new CadPointD(
            viewWidth * 0.5 - center.X * zoom,
            viewHeight * 0.5 - center.Y * zoom);

        return new FitView(zoom, offset);
    }

    private static CadRectD GetVisibleEntityBounds(CadDocument document)
    {
        var bounds = CadRectD.Empty;

        foreach (var entity in document.Entities.Values)
        {
            if (!CanFitEntity(document, entity) || entity.Bounds.IsEmpty)
                continue;

            bounds = bounds.Union(entity.Bounds);
        }

        return bounds;
    }

    private static bool CanFitEntity(CadDocument document, CadEntity entity)
    {
        return !entity.IsErased &&
               entity.IsVisible &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is not null &&
               layer.IsVisible &&
               !layer.IsFrozen;
    }

    private readonly record struct FitView(double Zoom, CadPointD Offset);
}
