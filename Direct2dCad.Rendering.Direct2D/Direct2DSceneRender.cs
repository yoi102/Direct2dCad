using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

public sealed class Direct2DSceneRender : CadRender, ICadGeometryResourceManager, IDisposable
{
    private const double TwoPi = Math.PI * 2.0;
    private const double FullCircleTolerance = 1e-9;
    private readonly Direct2DResourceCache _resourceCache = new();
    private bool _disposed;

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);
        ThrowIfDisposed();
        _resourceCache.ApplyChanges(document, changes);
    }

    public void ResetDeviceResources(
        ID2D1Factory? factory,
        IDWriteFactory? writeFactory,
        ID2D1DeviceContext? deviceContext,
        CadDocument? document = null)
    {
        ThrowIfDisposed();
        _resourceCache.ResetDeviceResources(factory, writeFactory, deviceContext, document);
    }

    public void RebuildResources(CadDocument document)
    {
        RebuildAll(document);
    }

    public void RebuildAll(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _resourceCache.RebuildAll(document);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _resourceCache.RebuildEntityResources(document, entityId);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        _resourceCache.RemoveEntity(entityId);
    }

    public override void Render(CadDocument document, CadViewport viewport, CadRenderOptions? options = null)
    {
        Render(document, viewport, null, null, options);
    }

    public void Render(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene = null,
        CadRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();

        var deviceContext = _resourceCache.DeviceContext;
        if (deviceContext is null)
            return;

        options ??= new CadRenderOptions();

        var previousTransform = deviceContext.Transform;
        var previousAntialiasMode = deviceContext.AntialiasMode;
        var previousTextAntialiasMode = deviceContext.TextAntialiasMode;
        deviceContext.Transform = CreateViewportTransform(viewport);
        deviceContext.AntialiasMode = options.IsAntialiasingEnabled
            ? AntialiasMode.PerPrimitive
            : AntialiasMode.Aliased;
        deviceContext.TextAntialiasMode = options.IsTextAntialiasingEnabled
            ? Vortice.Direct2D1.TextAntialiasMode.Default
            : Vortice.Direct2D1.TextAntialiasMode.Aliased;

        try
        {
            if (options.DrawGrid)
                DrawGrid(deviceContext, document, viewport, options.DirtyWorldBounds);

            if (options.DrawOrigin)
                DrawOrigin(deviceContext, document, viewport, options.DirtyWorldBounds);

            foreach (var entity in EnumerateDrawableEntities(document, viewport, options))
            {
                if (!_resourceCache.TryGetEntityResources(entity.Id, out var resources) || resources is null)
                    continue;

                DrawEntity(deviceContext, document, entity, resources, viewport, options);
            }

            DrawTransients(deviceContext, document, viewport, transientScene);
            DrawHandles(deviceContext, document, viewport, handleScene);
        }
        finally
        {
            deviceContext.TextAntialiasMode = previousTextAntialiasMode;
            deviceContext.AntialiasMode = previousAntialiasMode;
            deviceContext.Transform = previousTransform;
        }
    }

    private void DrawGrid(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadRectD? dirtyWorldBounds)
    {
        var grid = document.ViewSettings.Grid;
        if (grid.Type == CadGridType.None)
            return;

        var bounds = ResolveRenderWorldBounds(viewport, dirtyWorldBounds);
        if (bounds.IsEmpty)
            return;

        var snapSpacingX = grid.GetSnapSpacingX();
        var snapSpacingY = grid.GetSnapSpacingY();
        var spacingX = ResolveGridSpacing(
            grid.SpacingX,
            grid.Subdivision,
            grid.MinimumScreenSpacing,
            grid.MinimumWorldSpacing,
            snapSpacingX,
            viewport.Zoom);
        var spacingY = ResolveGridSpacing(
            grid.SpacingY,
            grid.Subdivision,
            grid.MinimumScreenSpacing,
            grid.MinimumWorldSpacing,
            snapSpacingY,
            viewport.Zoom);
        if (spacingX <= 0 || spacingY <= 0)
            return;

        var majorX = ResolveMajorGridSpacing(grid.SpacingX, spacingX, grid.Subdivision);
        var majorY = ResolveMajorGridSpacing(grid.SpacingY, spacingY, grid.Subdivision);
        var gridOrigin = document.ViewSettings.Origin.Position;
        var palette = CreateGridPalette(grid);
        var minorStroke = (float)(palette.MinorStrokeWidth / Math.Max(viewport.Zoom, double.Epsilon));
        var majorStroke = (float)(palette.MajorStrokeWidth / Math.Max(viewport.Zoom, double.Epsilon));

        using var minorBrush = CreateTransientBrush(deviceContext, palette.MinorColor);
        using var majorBrush = CreateTransientBrush(deviceContext, palette.MajorColor);

        switch (grid.Type)
        {
            case CadGridType.Dots:
                DrawGridDots(deviceContext, bounds, gridOrigin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;

            case CadGridType.Cross:
                DrawGridCrosses(deviceContext, bounds, gridOrigin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;

            default:
                DrawGridLines(deviceContext, bounds, gridOrigin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;
        }
    }

    private void DrawOrigin(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadRectD? dirtyWorldBounds)
    {
        var origin = document.ViewSettings.Origin;
        if (origin.DisplayType == CadOriginDisplayType.None)
            return;

        var bounds = ResolveRenderWorldBounds(viewport, dirtyWorldBounds);
        if (bounds.IsEmpty)
            return;

        var style = new CadTransientStyle(
            origin.Color,
            GuardScreenStroke(origin.StrokeWidth, 0.62),
            ToTransientLinePattern(origin.LinePattern));

        if (origin.DisplayType is CadOriginDisplayType.Axes or CadOriginDisplayType.AxesAndMarker)
            DrawOriginAxes(deviceContext, viewport, bounds, origin.Position, style);

        if (origin.DisplayType is CadOriginDisplayType.Marker or CadOriginDisplayType.AxesAndMarker)
            DrawOriginMarker(deviceContext, viewport, origin, style);
    }

    private static void DrawGridLines(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadPointD origin,
        double spacingX,
        double spacingY,
        double majorX,
        double majorY,
        ID2D1Brush minorBrush,
        ID2D1Brush majorBrush,
        float minorStroke,
        float majorStroke)
    {
        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        {
            var isMajor = IsNearGridLine(x, origin.X, majorX);
            var brush = isMajor ? majorBrush : minorBrush;
            var stroke = isMajor ? majorStroke : minorStroke;
            deviceContext.DrawLine(
                new Vector2((float)x, (float)bounds.MinY),
                new Vector2((float)x, (float)bounds.MaxY),
                brush,
                stroke);
        }

        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var isMajor = IsNearGridLine(y, origin.Y, majorY);
            var brush = isMajor ? majorBrush : minorBrush;
            var stroke = isMajor ? majorStroke : minorStroke;
            deviceContext.DrawLine(
                new Vector2((float)bounds.MinX, (float)y),
                new Vector2((float)bounds.MaxX, (float)y),
                brush,
                stroke);
        }
    }

    private static void DrawGridDots(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadPointD origin,
        double spacingX,
        double spacingY,
        double majorX,
        double majorY,
        ID2D1Brush minorBrush,
        ID2D1Brush majorBrush,
        float minorStroke,
        float majorStroke)
    {
        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        {
            foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
            {
                var isMajor = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
                var size = isMajor ? majorStroke * 1.7f : minorStroke * 1.25f;
                var brush = isMajor ? majorBrush : minorBrush;
                deviceContext.FillRectangle(
                    new RawRectF(
                        (float)x - size,
                        (float)y - size,
                        (float)x + size,
                        (float)y + size),
                    brush);
            }
        }
    }

    private static void DrawGridCrosses(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadPointD origin,
        double spacingX,
        double spacingY,
        double majorX,
        double majorY,
        ID2D1Brush minorBrush,
        ID2D1Brush majorBrush,
        float minorStroke,
        float majorStroke)
    {
        var armX = spacingX * 0.12;
        var armY = spacingY * 0.12;

        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        {
            foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
            {
                var isMajor = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
                var brush = isMajor ? majorBrush : minorBrush;
                var stroke = isMajor ? majorStroke : minorStroke;
                deviceContext.DrawLine(
                    new Vector2((float)(x - armX), (float)y),
                    new Vector2((float)(x + armX), (float)y),
                    brush,
                    stroke);
                deviceContext.DrawLine(
                    new Vector2((float)x, (float)(y - armY)),
                    new Vector2((float)x, (float)(y + armY)),
                    brush,
                    stroke);
            }
        }
    }

    private void DrawOriginAxes(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadRectD bounds,
        CadPointD originPosition,
        CadTransientStyle style)
    {
        if (bounds.MinX <= originPosition.X && bounds.MaxX >= originPosition.X)
        {
            DrawTransientLine(
                deviceContext,
                viewport,
                new CadPointD(originPosition.X, bounds.MinY),
                new CadPointD(originPosition.X, bounds.MaxY),
                style);
        }

        if (bounds.MinY <= originPosition.Y && bounds.MaxY >= originPosition.Y)
        {
            DrawTransientLine(
                deviceContext,
                viewport,
                new CadPointD(bounds.MinX, originPosition.Y),
                new CadPointD(bounds.MaxX, originPosition.Y),
                style);
        }
    }

    private void DrawOriginMarker(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadOriginSettings origin,
        CadTransientStyle style)
    {
        var halfSize = GuardScreenStroke(origin.Size, 18.0) * 0.5 / Math.Max(viewport.Zoom, double.Epsilon);
        var center = origin.Position;

        switch (origin.MarkerType)
        {
            case CadOriginMarkerType.X:
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    new CadPointD(center.X - halfSize, center.Y - halfSize),
                    new CadPointD(center.X + halfSize, center.Y + halfSize),
                    style);
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    new CadPointD(center.X - halfSize, center.Y + halfSize),
                    new CadPointD(center.X + halfSize, center.Y - halfSize),
                    style);
                break;

            case CadOriginMarkerType.Circle:
                DrawTransientCircle(deviceContext, viewport, center, halfSize, style);
                break;

            case CadOriginMarkerType.Square:
                DrawTransientRectangle(
                    deviceContext,
                    viewport,
                    CadRectD.FromLTRB(
                        center.X - halfSize,
                        center.Y - halfSize,
                        center.X + halfSize,
                        center.Y + halfSize),
                    style);
                break;

            default:
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    new CadPointD(center.X - halfSize, center.Y),
                    new CadPointD(center.X + halfSize, center.Y),
                    style);
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    new CadPointD(center.X, center.Y - halfSize),
                    new CadPointD(center.X, center.Y + halfSize),
                    style);
                break;
        }
    }

    private static double ResolveGridSpacing(
        double configuredMajorSpacing,
        int subdivision,
        double minimumScreenSpacing,
        double minimumWorldSpacing,
        double snapSpacing,
        double zoom)
    {
        if (configuredMajorSpacing <= 0 || double.IsNaN(configuredMajorSpacing) || double.IsInfinity(configuredMajorSpacing))
            configuredMajorSpacing = 10.0;

        var factor = Math.Max(2, subdivision);
        var snap = snapSpacing > 0 && !double.IsNaN(snapSpacing) && !double.IsInfinity(snapSpacing)
            ? snapSpacing
            : 1.0;
        var minWorld = minimumWorldSpacing > 0 && !double.IsNaN(minimumWorldSpacing) && !double.IsInfinity(minimumWorldSpacing)
            ? minimumWorldSpacing
            : 1.0;
        var minSpacing = CeilToMultiple(Math.Max(minWorld, snap), snap);
        var spacing = CeilToMultiple(Math.Max(configuredMajorSpacing / factor, minSpacing), snap);
        var minPixels = minimumScreenSpacing > 0 ? minimumScreenSpacing : 28.0;

        while (spacing * zoom < minPixels)
            spacing *= factor;

        while (spacing * zoom > minPixels * factor)
        {
            var next = CeilToMultiple(spacing / factor, snap);
            if (next < minSpacing || Math.Abs(next - spacing) < snap * 1e-9)
            {
                spacing = minSpacing;
                break;
            }

            spacing = next;
        }

        return Math.Max(spacing, minSpacing);
    }

    private static double ResolveMajorGridSpacing(
        double configuredMajorSpacing,
        double displaySpacing,
        int subdivision)
    {
        var factor = Math.Max(2, subdivision);
        if (displaySpacing <= 0 || double.IsNaN(displaySpacing) || double.IsInfinity(displaySpacing))
            return 1.0;

        if (configuredMajorSpacing <= 0 || double.IsNaN(configuredMajorSpacing) || double.IsInfinity(configuredMajorSpacing))
            return displaySpacing * factor;

        return configuredMajorSpacing > displaySpacing
            ? CeilToMultiple(configuredMajorSpacing, displaySpacing)
            : displaySpacing * factor;
    }

    private static double CeilToMultiple(double value, double unit)
    {
        if (unit <= 0 || double.IsNaN(unit) || double.IsInfinity(unit))
            return value;

        return Math.Ceiling((value / unit) - 1e-9) * unit;
    }

    private static IEnumerable<double> EnumerateGridCoordinates(double min, double max, double spacing, double origin)
    {
        if (spacing <= 0 || double.IsNaN(spacing) || double.IsInfinity(spacing))
            yield break;

        const int maxLines = 900;
        var start = origin + Math.Floor((min - origin) / spacing) * spacing;
        var end = origin + Math.Ceiling((max - origin) / spacing) * spacing;
        var count = 0;

        for (var value = start; value <= end && count < maxLines; value += spacing, count++)
            yield return Math.Abs(value - origin) < spacing * 1e-9 ? origin : value;
    }

    private static bool IsNearGridLine(double value, double origin, double spacing)
    {
        if (spacing <= 0)
            return false;

        var quotient = (value - origin) / spacing;
        return Math.Abs(quotient - Math.Round(quotient)) < 1e-6;
    }

    private static GridPalette CreateGridPalette(CadGridSettings grid)
    {
        return new GridPalette(
            grid.MinorLineColor,
            grid.MajorLineColor,
            GuardScreenStroke(grid.MinorLineWidth, 0.22),
            GuardScreenStroke(grid.MajorLineWidth, 0.36));
    }

    private static CadTransientLinePattern ToTransientLinePattern(CadOriginLinePattern pattern)
    {
        return pattern switch
        {
            CadOriginLinePattern.Dash => CadTransientLinePattern.Dash,
            CadOriginLinePattern.Dot => CadTransientLinePattern.Dot,
            CadOriginLinePattern.DashDot => CadTransientLinePattern.DashDot,
            _ => CadTransientLinePattern.Solid
        };
    }

    private static double GuardScreenStroke(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private void DrawTransients(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene)
    {
        if (scene is null || scene.IsEmpty)
            return;

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case CadTransientLine line:
                    DrawTransientLine(deviceContext, viewport, line.Start, line.End, line.Style);
                    break;

                case CadTransientCircle circle when circle.Radius > 0:
                    DrawTransientCircle(deviceContext, viewport, circle.Center, circle.Radius, circle.Style);
                    break;

                case CadTransientEllipse ellipse when ellipse.RadiusX > 0 && ellipse.RadiusY > 0:
                    DrawTransientEllipse(
                        deviceContext,
                        viewport,
                        ellipse.Center,
                        ellipse.RadiusX,
                        ellipse.RadiusY,
                        ellipse.Style);
                    break;

                case CadTransientArc arc when arc.Radius > 0 && Math.Abs(arc.SweepAngleRadians) > double.Epsilon:
                    DrawTransientArc(
                        deviceContext,
                        viewport,
                        arc.Center,
                        arc.Radius,
                        arc.StartAngleRadians,
                        arc.SweepAngleRadians,
                        arc.Style);
                    break;

                case CadTransientPolyline polyline when polyline.Points.Count >= 2:
                    DrawTransientPolyline(
                        deviceContext,
                        viewport,
                        polyline.Points,
                        polyline.Closed,
                        polyline.Style);
                    break;

                case CadTransientSpline spline when spline.FitPoints.Count >= 2:
                    DrawTransientSpline(
                        deviceContext,
                        viewport,
                        spline.FitPoints,
                        spline.Closed,
                        spline.Style);
                    break;

                case CadTransientRectangle rectangle when !rectangle.Bounds.IsEmpty:
                    DrawTransientRectangle(deviceContext, viewport, rectangle.Bounds, rectangle.Style);
                    break;

                case CadTransientText text when !string.IsNullOrEmpty(text.Text) && text.Height > 0 && !text.Bounds.IsEmpty:
                    DrawTransientText(
                        deviceContext,
                        viewport,
                        text.Text,
                        text.Position,
                        text.Height,
                        text.Bounds,
                        text.Style,
                        text.IsInverted,
                        document.ViewSettings.BackgroundColor,
                        text.InvertedMarginFactor);
                    break;

                case CadTransientShapeText text when text.Height > 0:
                    DrawTransientShapeText(
                        deviceContext,
                        viewport,
                        text.Text,
                        text.Position,
                        text.Height,
                        text.RotationRadians,
                        text.WidthFactor,
                        text.CharacterSpacingFactor,
                        text.ObliqueAngleRadians,
                        text.Style,
                        text.IsInverted,
                        document.ViewSettings.BackgroundColor,
                        text.InvertedMarginFactor,
                        text.ShapeFontId);
                    break;

                case CadTransientEntityReference reference:
                    DrawTransientEntityReference(deviceContext, document, viewport, reference);
                    break;
            }
        }
    }

    private void DrawHandles(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene? scene)
    {
        if (scene is null || scene.IsEmpty)
            return;

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case CadSelectionEntityReference reference:
                    DrawSelectionEntityReference(deviceContext, document, viewport, reference);
                    break;

                case CadGripHandle grip:
                    DrawGripHandle(deviceContext, viewport, grip);
                    break;
            }
        }
    }

    private void DrawSelectionEntityReference(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference)
    {
        var style = ToTransientStyle(reference.Style, includeFill: false);
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        switch (entity)
        {
            case CadLine line:
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    line.Start + reference.Offset,
                    line.End + reference.Offset,
                    style);
                break;

            case CadCircle circle:
                DrawTransientCircle(
                    deviceContext,
                    viewport,
                    circle.Center + reference.Offset,
                    circle.Radius,
                    style);
                break;

            case CadEllipse ellipse:
                DrawTransientEllipse(
                    deviceContext,
                    viewport,
                    ellipse.Center + reference.Offset,
                    ellipse.RadiusX,
                    ellipse.RadiusY,
                    style);
                break;

            case CadArc arc:
                DrawTransientArc(
                    deviceContext,
                    viewport,
                    arc.Center + reference.Offset,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;

            case CadPolyline polyline:
                DrawTransientPolyline(
                    deviceContext,
                    viewport,
                    polyline.Points.Select(x => x + reference.Offset).ToArray(),
                    polyline.Closed,
                    style);
                break;

            case CadSpline spline:
                DrawTransientSpline(
                    deviceContext,
                    viewport,
                    spline.FitPoints.Select(x => x + reference.Offset).ToArray(),
                    spline.Closed,
                    style);
                break;

            case CadShapeText shapeText:
                DrawTransientShapeText(
                    deviceContext,
                    viewport,
                    shapeText.Text,
                    shapeText.Position + reference.Offset,
                    shapeText.Height,
                    shapeText.RotationRadians,
                    shapeText.WidthFactor,
                    shapeText.CharacterSpacingFactor,
                    shapeText.ObliqueAngleRadians,
                    style,
                    shapeFontId: shapeText.ShapeFontId);
                break;

            default:
                DrawTransientRectangle(
                    deviceContext,
                    viewport,
                    entity.Bounds.Translate(reference.Offset),
                    style);
                break;
        }
    }

    private void DrawGripHandle(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadGripHandle grip)
    {
        var halfSize = ResolveHandleHalfSize(grip.Style, viewport);
        if (halfSize <= 0)
            return;

        var bounds = CadRectD.FromLTRB(
            grip.Position.X - halfSize,
            grip.Position.Y - halfSize,
            grip.Position.X + halfSize,
            grip.Position.Y + halfSize);

        switch (grip.Style.Shape)
        {
            case CadHandleShape.Circle:
                DrawHandleCircle(deviceContext, bounds, grip.Style, viewport);
                break;

            case CadHandleShape.Diamond:
                DrawHandleDiamond(deviceContext, bounds, grip.Style, viewport);
                break;

            default:
                DrawHandleRectangle(deviceContext, bounds, grip.Style, viewport);
                break;
        }
    }

    private void DrawHandleRectangle(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadHandleStyle style,
        CadViewport viewport)
    {
        DrawTransientRectangle(
            deviceContext,
            viewport,
            bounds,
            ToTransientStyle(style, includeFill: true));
    }

    private void DrawHandleCircle(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadHandleStyle style,
        CadViewport viewport)
    {
        DrawTransientCircle(
            deviceContext,
            viewport,
            bounds.Center,
            bounds.Width * 0.5,
            ToTransientStyle(style, includeFill: true));
    }

    private void DrawHandleDiamond(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadHandleStyle style,
        CadViewport viewport)
    {
        var points = new[]
        {
            new Vector2((float)bounds.Center.X, (float)bounds.MinY),
            new Vector2((float)bounds.MaxX, (float)bounds.Center.Y),
            new Vector2((float)bounds.Center.X, (float)bounds.MaxY),
            new Vector2((float)bounds.MinX, (float)bounds.Center.Y)
        };

        if (!style.FillColor.IsTransparent && _resourceCache.Factory is not null)
        {
            using var geometry = CreatePolygonGeometry(points);
            using var fillBrush = CreateTransientBrush(deviceContext, style.FillColor);
            deviceContext.FillGeometry(geometry, fillBrush);
        }

        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        var strokeWidth = ResolveHandleStrokeWidth(style, viewport);
        for (var i = 0; i < points.Length; i++)
            deviceContext.DrawLine(points[i], points[(i + 1) % points.Length], brush, strokeWidth);
    }

    private ID2D1PathGeometry CreatePolygonGeometry(IReadOnlyList<Vector2> points)
    {
        var geometry = _resourceCache.Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(points[0], FigureBegin.Filled);

        for (var i = 1; i < points.Count; i++)
            sink.AddLine(points[i]);

        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return geometry;
    }

    private static CadTransientStyle ToTransientStyle(CadHandleStyle style, bool includeFill)
    {
        return new CadTransientStyle(
            style.StrokeColor,
            style.StrokeWidth,
            CadTransientLinePattern.Solid,
            includeFill ? style.FillColor : null,
            style.KeepSizeScreenConstant);
    }

    private static double ResolveHandleHalfSize(CadHandleStyle style, CadViewport viewport)
    {
        var size = Math.Max(style.Size, 0.0);
        return style.KeepSizeScreenConstant
            ? size * 0.5 / Math.Max(viewport.Zoom, double.Epsilon)
            : size * 0.5;
    }

    private static float ResolveHandleStrokeWidth(CadHandleStyle style, CadViewport viewport)
    {
        var width = Math.Max(style.StrokeWidth, 0.1);
        return style.KeepSizeScreenConstant
            ? (float)(width / Math.Max(viewport.Zoom, double.Epsilon))
            : (float)width;
    }

    private void DrawTransientEntityReference(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadTransientEntityReference reference)
    {
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        switch (entity)
        {
            case CadLine line:
                DrawTransientLine(
                    deviceContext,
                    viewport,
                    line.Start + reference.Offset,
                    line.End + reference.Offset,
                    reference.Style);
                break;

            case CadCircle circle:
                DrawTransientCircle(
                    deviceContext,
                    viewport,
                    circle.Center + reference.Offset,
                    circle.Radius,
                    reference.Style);
                break;

            case CadEllipse ellipse:
                DrawTransientEllipse(
                    deviceContext,
                    viewport,
                    ellipse.Center + reference.Offset,
                    ellipse.RadiusX,
                    ellipse.RadiusY,
                    reference.Style);
                break;

            case CadArc arc:
                DrawTransientArc(
                    deviceContext,
                    viewport,
                    arc.Center + reference.Offset,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    reference.Style);
                break;

            case CadPolyline polyline:
                DrawTransientPolyline(
                    deviceContext,
                    viewport,
                    polyline.Points.Select(x => x + reference.Offset).ToArray(),
                    polyline.Closed,
                    reference.Style);
                break;

            case CadSpline spline:
                DrawTransientSpline(
                    deviceContext,
                    viewport,
                    spline.FitPoints.Select(x => x + reference.Offset).ToArray(),
                    spline.Closed,
                    reference.Style);
                break;

            case CadShapeText shapeText:
                DrawTransientShapeText(
                    deviceContext,
                    viewport,
                    shapeText.Text,
                    shapeText.Position + reference.Offset,
                    shapeText.Height,
                    shapeText.RotationRadians,
                    shapeText.WidthFactor,
                    shapeText.CharacterSpacingFactor,
                    shapeText.ObliqueAngleRadians,
                    reference.Style,
                    shapeText.IsInverted,
                    document.ViewSettings.BackgroundColor,
                    shapeText.InvertedMarginFactor,
                    shapeText.ShapeFontId);
                break;

            case CadText text:
                var bounds = text.TextBounds.Translate(reference.Offset);
                DrawTransientText(
                    deviceContext,
                    viewport,
                    text.Text,
                    text.Position + reference.Offset,
                    text.Height,
                    bounds,
                    reference.Style,
                    text.IsInverted,
                    document.ViewSettings.BackgroundColor,
                    text.InvertedMarginFactor);
                DrawTransientRectangle(
                    deviceContext,
                    viewport,
                    text.IsInverted ? bounds.Inflate(text.GetInvertedMargin()) : bounds,
                    reference.Style with { FillColor = null });
                break;

            default:
                DrawTransientRectangle(
                    deviceContext,
                    viewport,
                    entity.Bounds.Translate(reference.Offset),
                    reference.Style);
                break;
        }
    }

    private void DrawTransientLine(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadPointD start,
        CadPointD end,
        CadTransientStyle style)
    {
        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        deviceContext.DrawLine(
            ToVector2(start),
            ToVector2(end),
            brush,
            ResolveTransientStrokeWidth(style, viewport),
            strokeStyle);
    }

    private void DrawTransientPolyline(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        IReadOnlyList<CadPointD> points,
        bool closed,
        CadTransientStyle style)
    {
        if (points.Count < 2)
            return;

        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        var strokeWidth = ResolveTransientStrokeWidth(style, viewport);

        if (_resourceCache.Factory is null)
        {
            for (var i = 1; i < points.Count; i++)
                deviceContext.DrawLine(ToVector2(points[i - 1]), ToVector2(points[i]), brush, strokeWidth, strokeStyle);

            if (closed && points.Count > 2)
                deviceContext.DrawLine(ToVector2(points[^1]), ToVector2(points[0]), brush, strokeWidth, strokeStyle);

            return;
        }

        using var geometry = CreateTransientPolylineGeometry(points, closed);
        if (closed &&
            style.FillColor is { } fillColor &&
            !fillColor.IsTransparent)
        {
            using var fillBrush = CreateTransientBrush(deviceContext, fillColor);
            deviceContext.FillGeometry(geometry, fillBrush);
        }

        deviceContext.DrawGeometry(geometry, brush, strokeWidth, strokeStyle);
    }

    private ID2D1PathGeometry CreateTransientPolylineGeometry(
        IReadOnlyList<CadPointD> points,
        bool closed)
    {
        var geometry = _resourceCache.Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(points[0]), closed ? FigureBegin.Filled : FigureBegin.Hollow);

        for (var i = 1; i < points.Count; i++)
            sink.AddLine(ToVector2(points[i]));

        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private void DrawTransientSpline(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        IReadOnlyList<CadPointD> fitPoints,
        bool closed,
        CadTransientStyle style)
    {
        if (_resourceCache.Factory is null || fitPoints.Count < 2)
            return;

        using var geometry = CreateTransientSplineGeometry(fitPoints, closed);
        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        deviceContext.DrawGeometry(
            geometry,
            brush,
            ResolveTransientStrokeWidth(style, viewport),
            strokeStyle);
    }

    private ID2D1PathGeometry CreateTransientSplineGeometry(
        IReadOnlyList<CadPointD> fitPoints,
        bool closed)
    {
        var geometry = _resourceCache.Factory!.CreatePathGeometry();
        var segments = CadSpline.CreateBezierSegments(fitPoints, closed);
        if (segments.Count == 0)
            return geometry;

        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(segments[0].Start), FigureBegin.Hollow);

        foreach (var segment in segments)
        {
            sink.AddBezier(new BezierSegment(
                ToVector2(segment.Control1),
                ToVector2(segment.Control2),
                ToVector2(segment.End)));
        }

        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private void DrawTransientArc(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        CadTransientStyle style)
    {
        if (_resourceCache.Factory is null ||
            radius <= 0 ||
            Math.Abs(sweepAngleRadians) <= double.Epsilon)
        {
            return;
        }

        using var geometry = CreateTransientArcGeometry(center, radius, startAngleRadians, sweepAngleRadians);
        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        deviceContext.DrawGeometry(
            geometry,
            brush,
            ResolveTransientStrokeWidth(style, viewport),
            strokeStyle);
    }

    private ID2D1PathGeometry CreateTransientArcGeometry(
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        var geometry = _resourceCache.Factory!.CreatePathGeometry();
        using var sink = geometry.Open();
        var startPoint = GetArcPoint(center, radius, startAngleRadians);
        sink.BeginFigure(ToVector2(startPoint), FigureBegin.Hollow);

        if (IsFullCircleSweep(sweepAngleRadians))
        {
            var halfSweep = sweepAngleRadians >= 0 ? Math.PI : -Math.PI;
            var midPoint = GetArcPoint(center, radius, startAngleRadians + halfSweep);
            sink.AddArc(CreateArcSegment(midPoint, radius, halfSweep));
            sink.AddArc(CreateArcSegment(startPoint, radius, halfSweep));
        }
        else
        {
            var endPoint = GetArcPoint(center, radius, startAngleRadians + sweepAngleRadians);
            sink.AddArc(CreateArcSegment(endPoint, radius, sweepAngleRadians));
        }

        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private static ArcSegment CreateArcSegment(
        CadPointD endPoint,
        double radius,
        double sweepAngleRadians)
    {
        return new ArcSegment(
            ToVector2(endPoint),
            new Size((float)radius, (float)radius),
            rotationAngle: 0,
            ToD2DSweepDirection(sweepAngleRadians),
            Math.Abs(sweepAngleRadians) > Math.PI ? ArcSize.Large : ArcSize.Small);
    }

    private static SweepDirection ToD2DSweepDirection(double sweepAngleRadians)
    {
        // The current viewport keeps Y increasing downward, so this maps to the
        // same visual direction as CadArc.GetPointAtAngle's Y + sin(angle).
        return sweepAngleRadians >= 0
            ? SweepDirection.Clockwise
            : SweepDirection.CounterClockwise;
    }

    private static bool IsFullCircleSweep(double sweepAngleRadians)
    {
        return Math.Abs(Math.Abs(sweepAngleRadians) - TwoPi) <= FullCircleTolerance;
    }

    private static CadPointD GetArcPoint(CadPointD center, double radius, double angleRadians)
    {
        return new CadPointD(
            center.X + Math.Cos(angleRadians) * radius,
            center.Y + Math.Sin(angleRadians) * radius);
    }

    private void DrawTransientCircle(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadPointD center,
        double radius,
        CadTransientStyle style)
    {
        DrawTransientEllipse(deviceContext, viewport, center, radius, radius, style);
    }

    private void DrawTransientEllipse(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadPointD center,
        double radiusX,
        double radiusY,
        CadTransientStyle style)
    {
        var ellipse = new Ellipse(ToVector2(center), (float)radiusX, (float)radiusY);

        if (style.FillColor is { } fillColor && !fillColor.IsTransparent)
        {
            using var fillBrush = CreateTransientBrush(deviceContext, fillColor);
            deviceContext.FillEllipse(ellipse, fillBrush);
        }

        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        deviceContext.DrawEllipse(
            ellipse,
            brush,
            ResolveTransientStrokeWidth(style, viewport),
            strokeStyle);
    }

    private void DrawTransientRectangle(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadRectD bounds,
        CadTransientStyle style)
    {
        var rect = new RawRectF(
            (float)bounds.MinX,
            (float)bounds.MinY,
            (float)bounds.MaxX,
            (float)bounds.MaxY);

        if (style.FillColor is { } fillColor && !fillColor.IsTransparent)
        {
            using var fillBrush = CreateTransientBrush(deviceContext, fillColor);
            deviceContext.FillRectangle(rect, fillBrush);
        }

        using var brush = CreateTransientBrush(deviceContext, style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        deviceContext.DrawRectangle(
            rect,
            brush,
            ResolveTransientStrokeWidth(style, viewport),
            strokeStyle);
    }

    private void DrawTransientText(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        string text,
        CadPointD position,
        double height,
        CadRectD bounds,
        CadTransientStyle style,
        bool isInverted = false,
        CadColor? invertedTextColor = null,
        double invertedMarginFactor = CadText.DefaultInvertedMarginFactor)
    {
        if (_resourceCache.WriteFactory is null || bounds.IsEmpty)
            return;

        if (isInverted)
        {
            using var invertedFillBrush = CreateTransientBrush(deviceContext, style.StrokeColor);
            FillBounds(
                deviceContext,
                CreateInvertedBackgroundBounds(bounds, height, invertedMarginFactor),
                invertedFillBrush);
        }

        using var brush = CreateTransientBrush(
            deviceContext,
            isInverted ? invertedTextColor ?? CadColor.Black : style.StrokeColor);
        using var format = _resourceCache.WriteFactory.CreateTextFormat(
            "Meiryo",
            null,
            FontWeight.Normal,
            FontStyle.Normal,
            FontStretch.Normal,
            (float)(height * CadText.FontSizeScale),
            "ja-JP");
        format.TextAlignment = TextAlignment.Leading;
        format.ParagraphAlignment = ParagraphAlignment.Near;
        format.WordWrapping = WordWrapping.NoWrap;

        DrawTextClipped(
            deviceContext,
            text,
            format,
            position,
            bounds,
            brush);
    }

    private void DrawTransientShapeText(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        string text,
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians,
        CadTransientStyle style,
        bool isInverted = false,
        CadColor? invertedTextColor = null,
        double invertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
    {
        var shapeFont = CadShapeFontRegistry.GetOrDefault(shapeFontId);
        if (isInverted)
        {
            var bounds = CadStrokeFont.MeasureBounds(
                text,
                position,
                height,
                widthFactor,
                characterSpacingFactor,
                obliqueAngleRadians,
                rotationRadians,
                shapeFont.Id);

            if (!bounds.IsEmpty)
            {
                using var invertedFillBrush = CreateTransientBrush(deviceContext, style.StrokeColor);
                FillBounds(
                    deviceContext,
                    CreateInvertedBackgroundBounds(bounds, height, invertedMarginFactor),
                    invertedFillBrush);
            }
        }

        using var brush = CreateTransientBrush(
            deviceContext,
            isInverted ? invertedTextColor ?? CadColor.Black : style.StrokeColor);
        using var strokeStyle = CreateTransientStrokeStyle(style);
        var strokeWidth = ResolveTransientStrokeWidth(style, viewport);

        foreach (var segment in CadStrokeFont.CreateSegments(
                     text,
                     position,
                     height,
                     widthFactor,
                     characterSpacingFactor,
                     obliqueAngleRadians,
                     rotationRadians,
                     shapeFont.Id))
        {
            deviceContext.DrawLine(
                ToVector2(segment.Start),
                ToVector2(segment.End),
                brush,
                strokeWidth,
                strokeStyle);
        }
    }

    private ID2D1StrokeStyle? CreateTransientStrokeStyle(CadTransientStyle style)
    {
        if (style.LinePattern == CadTransientLinePattern.Solid || _resourceCache.Factory is null)
            return null;

        var dashStyle = style.LinePattern switch
        {
            CadTransientLinePattern.Dot => DashStyle.Dot,
            CadTransientLinePattern.DashDot => DashStyle.DashDot,
            _ => DashStyle.Dash
        };

        return _resourceCache.Factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            DashStyle = dashStyle
        });
    }

    private static ID2D1SolidColorBrush CreateTransientBrush(ID2D1DeviceContext deviceContext, CadColor color)
    {
        return deviceContext.CreateSolidColorBrush(ToColor4(color));
    }

    private static float ResolveTransientStrokeWidth(CadTransientStyle style, CadViewport viewport)
    {
        var width = Math.Max(style.StrokeWidth, 0.1);
        return style.KeepStrokeWidthScreenConstant
            ? (float)(width / Math.Max(viewport.Zoom, double.Epsilon))
            : (float)width;
    }

    private static IEnumerable<CadEntity> EnumerateDrawableEntities(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var dirtyWorldBounds = ResolveEntityDirtyWorldBounds(viewport, options);
        return document.Entities.Values
            .Select((entity, index) => new { Entity = entity, Index = index })
            .Where(x =>
                !x.Entity.IsErased &&
                x.Entity.IsVisible &&
                !options.HiddenEntityIds.Contains(x.Entity.Id) &&
                (dirtyWorldBounds is null || EntityIntersectsDirtyBounds(x.Entity, dirtyWorldBounds.Value)) &&
                document.TryGetLayer(x.Entity.LayerId, out var layer) &&
                layer is not null &&
                layer.IsVisible &&
                !layer.IsFrozen)
            .OrderBy(x => document.DocumentSettings.LayerDrawingPriority.GetPriority(x.Entity.LayerId))
            .ThenBy(x => x.Entity.ZIndex)
            .ThenBy(x => x.Entity.Id.Value)
            .Select(x => x.Entity);
    }

    private static CadRectD ResolveRenderWorldBounds(CadViewport viewport, CadRectD? dirtyWorldBounds)
    {
        if (dirtyWorldBounds is not { } dirty)
            return viewport.VisibleWorldBounds;

        return viewport.VisibleWorldBounds.Intersection(dirty);
    }

    private static CadRectD? ResolveEntityDirtyWorldBounds(
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (options.DirtyWorldBounds is not { } dirty || dirty.IsEmpty)
            return null;

        var padding = Math.Max(
            options.MinimumScreenStrokeWidth,
            options.KeepStrokeWidthScreenConstant ? 6.0 : 2.0) /
            Math.Max(viewport.Zoom, double.Epsilon);
        return dirty.Inflate(padding);
    }

    private static bool EntityIntersectsDirtyBounds(CadEntity entity, CadRectD dirtyWorldBounds)
    {
        return entity.Bounds.Intersects(dirtyWorldBounds) ||
               entity.Bounds.Contains(dirtyWorldBounds.Center) ||
               dirtyWorldBounds.Contains(entity.Bounds);
    }

    private static void DrawEntity(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (entity is CadShapeText { IsInverted: true } shapeText &&
            resources.Geometry is not null &&
            resources.StrokeBrush is not null)
        {
            FillBounds(deviceContext, shapeText.InvertedBackgroundBounds, resources.StrokeBrush);
            using var invertedBrush = CreateTransientBrush(deviceContext, document.ViewSettings.BackgroundColor);
            deviceContext.DrawGeometry(
                resources.Geometry,
                invertedBrush,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options));
            return;
        }

        if (resources.Geometry is not null && resources.FillBrush is not null)
            deviceContext.FillGeometry(resources.Geometry, resources.FillBrush);

        if (resources.Geometry is not null && resources.StrokeBrush is not null)
        {
            var strokeWidth = ResolveStrokeWidth(resources.StrokeWidth, viewport, options);
            deviceContext.DrawGeometry(resources.Geometry, resources.StrokeBrush, strokeWidth);
        }

        if (entity is CadText text &&
            resources.TextFormat is not null &&
            resources.StrokeBrush is not null)
        {
            if (text.IsInverted)
            {
                FillBounds(deviceContext, text.InvertedBackgroundBounds, resources.StrokeBrush);
                using var invertedBrush = CreateTransientBrush(deviceContext, document.ViewSettings.BackgroundColor);
                DrawTextClipped(
                    deviceContext,
                    text.Text,
                    resources.TextFormat,
                    text.Position,
                    text.TextBounds,
                    invertedBrush);
                return;
            }

            DrawTextClipped(
                deviceContext,
                text.Text,
                resources.TextFormat,
                text.Position,
                text.TextBounds,
                resources.StrokeBrush);
        }
    }

    private static CadRectD CreateInvertedBackgroundBounds(
        CadRectD textBounds,
        double height,
        double marginFactor)
    {
        if (textBounds.IsEmpty)
            return textBounds;

        var margin = height > 0 &&
                     marginFactor > 0 &&
                     !double.IsNaN(height) &&
                     !double.IsInfinity(height) &&
                     !double.IsNaN(marginFactor) &&
                     !double.IsInfinity(marginFactor)
            ? height * marginFactor
            : 0;

        return margin > 0 ? textBounds.Inflate(margin) : textBounds;
    }

    private static void FillBounds(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        ID2D1Brush brush)
    {
        if (bounds.IsEmpty)
            return;

        deviceContext.FillRectangle(
            new RawRectF(
                (float)bounds.MinX,
                (float)bounds.MinY,
                (float)bounds.MaxX,
                (float)bounds.MaxY),
            brush);
    }

    private static void DrawTextClipped(
        ID2D1DeviceContext deviceContext,
        string text,
        IDWriteTextFormat format,
        CadPointD layoutOrigin,
        CadRectD bounds,
        ID2D1Brush brush)
    {
        if (bounds.IsEmpty)
            return;

        var clip = new RawRectF(
            (float)bounds.MinX,
            (float)bounds.MinY,
            (float)bounds.MaxX,
            (float)bounds.MaxY);
        deviceContext.PushAxisAlignedClip(clip, AntialiasMode.PerPrimitive);

        try
        {
            deviceContext.DrawText(
                text,
                format,
                Rect.FromLTRB(
                    (float)layoutOrigin.X,
                    (float)layoutOrigin.Y,
                    (float)(layoutOrigin.X + Math.Max(bounds.Width, 1e-6)),
                    (float)(layoutOrigin.Y + Math.Max(bounds.Height, 1e-6))),
                brush,
                DrawTextOptions.Clip);
        }
        finally
        {
            deviceContext.PopAxisAlignedClip();
        }
    }

    private static System.Numerics.Matrix3x2 CreateViewportTransform(CadViewport viewport)
    {
        return System.Numerics.Matrix3x2.CreateScale((float)viewport.Zoom) *
               System.Numerics.Matrix3x2.CreateTranslation(
                   (float)viewport.Offset.X,
                   (float)viewport.Offset.Y);
    }

    private static float ResolveStrokeWidth(
        float modelStrokeWidth,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var strokeWidth = options.KeepStrokeWidthScreenConstant
            ? modelStrokeWidth / Math.Max((float)viewport.Zoom, float.Epsilon)
            : modelStrokeWidth;

        return Math.Max(strokeWidth, (float)options.MinimumScreenStrokeWidth / Math.Max((float)viewport.Zoom, float.Epsilon));
    }

    private static Vector2 ToVector2(CadPointD point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Color4 ToColor4(CadColor color)
    {
        return new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _resourceCache.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DSceneRender));
    }

    private readonly record struct GridPalette(
        CadColor MinorColor,
        CadColor MajorColor,
        double MinorStrokeWidth,
        double MajorStrokeWidth);
}
