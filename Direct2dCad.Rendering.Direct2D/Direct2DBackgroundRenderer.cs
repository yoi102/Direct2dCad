using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DBackgroundRenderer
{
    public void DrawGrid(
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

        var spacingX = ResolveGridSpacing(
            grid.SpacingX,
            grid.Subdivision,
            grid.MinimumScreenSpacing,
            grid.MinimumWorldSpacing,
            grid.GetSnapSpacingX(),
            viewport.Zoom);
        var spacingY = ResolveGridSpacing(
            grid.SpacingY,
            grid.Subdivision,
            grid.MinimumScreenSpacing,
            grid.MinimumWorldSpacing,
            grid.GetSnapSpacingY(),
            viewport.Zoom);
        if (spacingX <= 0 || spacingY <= 0)
            return;

        var majorX = ResolveMajorGridSpacing(grid.SpacingX, spacingX, grid.Subdivision);
        var majorY = ResolveMajorGridSpacing(grid.SpacingY, spacingY, grid.Subdivision);
        var origin = document.ViewSettings.Origin.Position;
        var palette = CreateGridPalette(grid);
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var minorStroke = (float)(palette.MinorStrokeWidth / zoom);
        var majorStroke = (float)(palette.MajorStrokeWidth / zoom);
        using var minorBrush = CreateBrush(deviceContext, palette.MinorColor);
        using var majorBrush = CreateBrush(deviceContext, palette.MajorColor);

        switch (grid.Type)
        {
            case CadGridType.Dots:
                DrawGridDots(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;
            case CadGridType.Cross:
                DrawGridCrosses(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;
            default:
                DrawGridLines(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, minorStroke, majorStroke);
                break;
        }
    }

    public void DrawOrigin(
        ID2D1DeviceContext deviceContext,
        ID2D1Factory? factory,
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

        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var strokeWidth = (float)(Math.Max(GuardScreenStroke(origin.StrokeWidth, 0.62), 0.5) / zoom);
        using var brush = CreateBrush(deviceContext, origin.Color);
        using var strokeStyle = CreateOriginStrokeStyle(factory, origin.LinePattern);

        if (origin.DisplayType is CadOriginDisplayType.Axes or CadOriginDisplayType.AxesAndMarker)
            DrawOriginAxes(deviceContext, bounds, origin.Position, brush, strokeWidth, strokeStyle);

        if (origin.DisplayType is CadOriginDisplayType.Marker or CadOriginDisplayType.AxesAndMarker)
            DrawOriginMarker(deviceContext, viewport, origin, brush, strokeWidth, strokeStyle);
    }

    private static void DrawOriginAxes(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadPointD origin,
        ID2D1Brush brush,
        float strokeWidth,
        ID2D1StrokeStyle? strokeStyle)
    {
        if (bounds.MinX <= origin.X && bounds.MaxX >= origin.X)
        {
            deviceContext.DrawLine(
                ToVector2(new CadPointD(origin.X, bounds.MinY)),
                ToVector2(new CadPointD(origin.X, bounds.MaxY)),
                brush,
                strokeWidth,
                strokeStyle);
        }

        if (bounds.MinY <= origin.Y && bounds.MaxY >= origin.Y)
        {
            deviceContext.DrawLine(
                ToVector2(new CadPointD(bounds.MinX, origin.Y)),
                ToVector2(new CadPointD(bounds.MaxX, origin.Y)),
                brush,
                strokeWidth,
                strokeStyle);
        }
    }

    private static void DrawOriginMarker(
        ID2D1DeviceContext deviceContext,
        CadViewport viewport,
        CadOriginSettings origin,
        ID2D1Brush brush,
        float strokeWidth,
        ID2D1StrokeStyle? strokeStyle)
    {
        var halfSize = GuardScreenStroke(origin.Size, 18.0) * 0.5 /
                       Math.Max(viewport.Zoom, double.Epsilon);
        var center = origin.Position;
        switch (origin.MarkerType)
        {
            case CadOriginMarkerType.X:
                DrawLine(center.X - halfSize, center.Y - halfSize, center.X + halfSize, center.Y + halfSize);
                DrawLine(center.X - halfSize, center.Y + halfSize, center.X + halfSize, center.Y - halfSize);
                break;
            case CadOriginMarkerType.Circle:
                deviceContext.DrawEllipse(
                    new Ellipse(ToVector2(center), (float)halfSize, (float)halfSize),
                    brush,
                    strokeWidth,
                    strokeStyle);
                break;
            case CadOriginMarkerType.Square:
                deviceContext.DrawRectangle(
                    new RawRectF(
                        (float)(center.X - halfSize),
                        (float)(center.Y - halfSize),
                        (float)(center.X + halfSize),
                        (float)(center.Y + halfSize)),
                    brush,
                    strokeWidth,
                    strokeStyle);
                break;
            default:
                DrawLine(center.X - halfSize, center.Y, center.X + halfSize, center.Y);
                DrawLine(center.X, center.Y - halfSize, center.X, center.Y + halfSize);
                break;
        }

        return;

        void DrawLine(double x1, double y1, double x2, double y2)
        {
            deviceContext.DrawLine(
                new Vector2((float)x1, (float)y1),
                new Vector2((float)x2, (float)y2),
                brush,
                strokeWidth,
                strokeStyle);
        }
    }

    private static ID2D1StrokeStyle? CreateOriginStrokeStyle(
        ID2D1Factory? factory,
        CadOriginLinePattern pattern)
    {
        if (factory is null || pattern == CadOriginLinePattern.Solid)
            return null;

        var dashStyle = pattern switch
        {
            CadOriginLinePattern.Dot => DashStyle.Dot,
            CadOriginLinePattern.DashDot => DashStyle.DashDot,
            _ => DashStyle.Dash
        };
        return factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            DashStyle = dashStyle
        });
    }

    private static void DrawGridLines(
        ID2D1DeviceContext context,
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
            var major = IsNearGridLine(x, origin.X, majorX);
            context.DrawLine(
                new Vector2((float)x, (float)bounds.MinY),
                new Vector2((float)x, (float)bounds.MaxY),
                major ? majorBrush : minorBrush,
                major ? majorStroke : minorStroke);
        }

        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(y, origin.Y, majorY);
            context.DrawLine(
                new Vector2((float)bounds.MinX, (float)y),
                new Vector2((float)bounds.MaxX, (float)y),
                major ? majorBrush : minorBrush,
                major ? majorStroke : minorStroke);
        }
    }

    private static void DrawGridDots(
        ID2D1DeviceContext context,
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
        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
            var size = major ? majorStroke * 1.7f : minorStroke * 1.25f;
            context.FillRectangle(
                new RawRectF((float)x - size, (float)y - size, (float)x + size, (float)y + size),
                major ? majorBrush : minorBrush);
        }
    }

    private static void DrawGridCrosses(
        ID2D1DeviceContext context,
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
        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
            var brush = major ? majorBrush : minorBrush;
            var stroke = major ? majorStroke : minorStroke;
            context.DrawLine(new Vector2((float)(x - armX), (float)y), new Vector2((float)(x + armX), (float)y), brush, stroke);
            context.DrawLine(new Vector2((float)x, (float)(y - armY)), new Vector2((float)x, (float)(y + armY)), brush, stroke);
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
        if (!IsPositiveFinite(configuredMajorSpacing))
            configuredMajorSpacing = 10.0;

        var factor = Math.Max(2, subdivision);
        var snap = IsPositiveFinite(snapSpacing) ? snapSpacing : 1.0;
        var minWorld = IsPositiveFinite(minimumWorldSpacing) ? minimumWorldSpacing : 1.0;
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

    private static double ResolveMajorGridSpacing(double configured, double display, int subdivision)
    {
        var factor = Math.Max(2, subdivision);
        if (!IsPositiveFinite(display))
            return 1.0;
        if (!IsPositiveFinite(configured))
            return display * factor;
        return configured > display ? CeilToMultiple(configured, display) : display * factor;
    }

    private static IEnumerable<double> EnumerateGridCoordinates(double min, double max, double spacing, double origin)
    {
        if (!IsPositiveFinite(spacing))
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

    private static double CeilToMultiple(double value, double unit)
    {
        return IsPositiveFinite(unit)
            ? Math.Ceiling((value / unit) - 1e-9) * unit
            : value;
    }

    private static GridPalette CreateGridPalette(CadGridSettings grid)
    {
        return new GridPalette(
            grid.MinorLineColor,
            grid.MajorLineColor,
            GuardScreenStroke(grid.MinorLineWidth, 0.22),
            GuardScreenStroke(grid.MajorLineWidth, 0.36));
    }

    private static CadRectD ResolveRenderWorldBounds(CadViewport viewport, CadRectD? dirtyBounds)
    {
        if (dirtyBounds is not { } dirty || dirty.IsEmpty)
            return viewport.VisibleWorldBounds;
        return viewport.VisibleWorldBounds.Intersection(dirty);
    }

    private static ID2D1SolidColorBrush CreateBrush(ID2D1DeviceContext context, CadColor color)
    {
        return context.CreateSolidColorBrush(new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f));
    }

    private static double GuardScreenStroke(double value, double fallback) => IsPositiveFinite(value) ? value : fallback;

    private static bool IsPositiveFinite(double value) => value > 0 && double.IsFinite(value);

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);

    private readonly record struct GridPalette(
        CadColor MinorColor,
        CadColor MajorColor,
        double MinorStrokeWidth,
        double MajorStrokeWidth);
}
