using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DBackgroundRenderer(Direct2DStyleResourceCache styleResources)
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
        var rasterization = GridRasterization.Create(
            viewport,
            deviceContext.AntialiasMode == AntialiasMode.Aliased,
            palette.MinorStrokeWidth,
            palette.MajorStrokeWidth);
        var minorBrush = styleResources.GetBrush(
            deviceContext,
            rasterization.ResolveColor(palette.MinorColor, palette.MinorStrokeWidth, major: false));
        var majorBrush = styleResources.GetBrush(
            deviceContext,
            rasterization.ResolveColor(palette.MajorColor, palette.MajorStrokeWidth, major: true));

        switch (grid.Type)
        {
            case CadGridType.Dots:
                DrawGridDots(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, rasterization);
                break;
            case CadGridType.Cross:
                DrawGridCrosses(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, rasterization);
                break;
            default:
                DrawGridLines(deviceContext, bounds, origin, spacingX, spacingY, majorX, majorY, minorBrush, majorBrush, rasterization);
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

        var rasterization = OriginRasterization.Create(
            viewport,
            deviceContext.AntialiasMode == AntialiasMode.Aliased,
            Math.Max(GuardScreenStroke(origin.StrokeWidth, 0.62), 0.5));
        var brush = styleResources.GetBrush(deviceContext, origin.Color);
        var strokeStyle = styleResources.GetOriginStrokeStyle(factory, origin.LinePattern);

        if (origin.DisplayType is CadOriginDisplayType.Axes or CadOriginDisplayType.AxesAndMarker)
            DrawOriginAxes(deviceContext, bounds, origin.Position, brush, rasterization, strokeStyle);

        if (origin.DisplayType is CadOriginDisplayType.Marker or CadOriginDisplayType.AxesAndMarker)
            DrawOriginMarker(deviceContext, origin, brush, rasterization, strokeStyle);
    }

    private static void DrawOriginAxes(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadPointD origin,
        ID2D1Brush brush,
        OriginRasterization rasterization,
        ID2D1StrokeStyle? strokeStyle)
    {
        var strokeWidth = rasterization.WorldStrokeWidth;
        var overlapPadding = strokeWidth * 0.5;
        if (bounds.MinX - overlapPadding <= origin.X && bounds.MaxX + overlapPadding >= origin.X)
        {
            var drawX = rasterization.AlignWorldX(origin.X);
            deviceContext.DrawLine(
                ToVector2(new CadPointD(drawX, bounds.MinY)),
                ToVector2(new CadPointD(drawX, bounds.MaxY)),
                brush,
                strokeWidth,
                strokeStyle);
        }

        if (bounds.MinY - overlapPadding <= origin.Y && bounds.MaxY + overlapPadding >= origin.Y)
        {
            var drawY = rasterization.AlignWorldY(origin.Y);
            deviceContext.DrawLine(
                ToVector2(new CadPointD(bounds.MinX, drawY)),
                ToVector2(new CadPointD(bounds.MaxX, drawY)),
                brush,
                strokeWidth,
                strokeStyle);
        }
    }

    private static void DrawOriginMarker(
        ID2D1DeviceContext deviceContext,
        CadOriginSettings origin,
        ID2D1Brush brush,
        OriginRasterization rasterization,
        ID2D1StrokeStyle? strokeStyle)
    {
        var halfSize = rasterization.ResolveMarkerHalfSize(GuardScreenStroke(origin.Size, 18.0));
        var center = rasterization.AlignPoint(origin.Position);
        var strokeWidth = rasterization.WorldStrokeWidth;
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
        GridRasterization rasterization)
    {
        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        {
            var major = IsNearGridLine(x, origin.X, majorX);
            var drawX = rasterization.AlignWorldX(x, major);
            context.DrawLine(
                new Vector2((float)drawX, (float)bounds.MinY),
                new Vector2((float)drawX, (float)bounds.MaxY),
                major ? majorBrush : minorBrush,
                rasterization.ResolveWorldStroke(major));
        }

        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(y, origin.Y, majorY);
            var drawY = rasterization.AlignWorldY(y, major);
            context.DrawLine(
                new Vector2((float)bounds.MinX, (float)drawY),
                new Vector2((float)bounds.MaxX, (float)drawY),
                major ? majorBrush : minorBrush,
                rasterization.ResolveWorldStroke(major));
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
        GridRasterization rasterization)
    {
        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
            var center = rasterization.AlignDot(new CadPointD(x, y), major);
            var size = rasterization.ResolveDotHalfSize(major);
            context.FillRectangle(
                new RawRectF(
                    (float)(center.X - size),
                    (float)(center.Y - size),
                    (float)(center.X + size),
                    (float)(center.Y + size)),
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
        GridRasterization rasterization)
    {
        var armX = spacingX * 0.12;
        var armY = spacingY * 0.12;
        foreach (var x in EnumerateGridCoordinates(bounds.MinX, bounds.MaxX, spacingX, origin.X))
        foreach (var y in EnumerateGridCoordinates(bounds.MinY, bounds.MaxY, spacingY, origin.Y))
        {
            var major = IsNearGridLine(x, origin.X, majorX) && IsNearGridLine(y, origin.Y, majorY);
            var brush = major ? majorBrush : minorBrush;
            var stroke = rasterization.ResolveWorldStroke(major);
            var drawX = rasterization.AlignWorldX(x, major);
            var drawY = rasterization.AlignWorldY(y, major);
            context.DrawLine(new Vector2((float)(drawX - armX), (float)drawY), new Vector2((float)(drawX + armX), (float)drawY), brush, stroke);
            context.DrawLine(new Vector2((float)drawX, (float)(drawY - armY)), new Vector2((float)drawX, (float)(drawY + armY)), brush, stroke);
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

    private static double GuardScreenStroke(double value, double fallback) => IsPositiveFinite(value) ? value : fallback;

    private static bool IsPositiveFinite(double value) => value > 0 && double.IsFinite(value);

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);

    private readonly record struct GridPalette(
        CadColor MinorColor,
        CadColor MajorColor,
        double MinorStrokeWidth,
        double MajorStrokeWidth);

    private readonly record struct OriginRasterization(
        CadViewport Viewport,
        bool IsAliased,
        float ScreenStrokeWidth)
    {
        public float WorldStrokeWidth => ScreenStrokeWidth /
                                         (float)Math.Max(Viewport.Zoom, double.Epsilon);

        public static OriginRasterization Create(
            CadViewport viewport,
            bool isAliased,
            double configuredScreenStrokeWidth)
        {
            var screenStrokeWidth = (float)configuredScreenStrokeWidth;
            if (isAliased)
            {
                screenStrokeWidth = Math.Max(
                    1.0f,
                    MathF.Round(screenStrokeWidth, MidpointRounding.AwayFromZero));
            }

            return new OriginRasterization(viewport, isAliased, screenStrokeWidth);
        }

        public double AlignWorldX(double worldX)
        {
            if (!IsAliased)
                return worldX;

            var screenX = worldX * Viewport.Zoom + Viewport.Offset.X;
            var alignedScreenX = AlignScreenCoordinate(screenX);
            return (alignedScreenX - Viewport.Offset.X) / Viewport.Zoom;
        }

        public double AlignWorldY(double worldY)
        {
            if (!IsAliased)
                return worldY;

            var screenY = Viewport.Offset.Y - worldY * Viewport.Zoom;
            var alignedScreenY = AlignScreenCoordinate(screenY);
            return (Viewport.Offset.Y - alignedScreenY) / Viewport.Zoom;
        }

        public CadPointD AlignPoint(CadPointD point)
        {
            return new CadPointD(AlignWorldX(point.X), AlignWorldY(point.Y));
        }

        public double ResolveMarkerHalfSize(double configuredScreenSize)
        {
            var screenSize = IsAliased
                ? Math.Max(1.0, Math.Round(configuredScreenSize, MidpointRounding.AwayFromZero))
                : configuredScreenSize;
            return screenSize * 0.5 / Math.Max(Viewport.Zoom, double.Epsilon);
        }

        private double AlignScreenCoordinate(double value)
        {
            var pixelSpan = Math.Max(1, (int)Math.Round(
                ScreenStrokeWidth,
                MidpointRounding.AwayFromZero));
            var phase = (pixelSpan & 1) == 0 ? 0.0 : 0.5;
            return Math.Floor(value - phase + 0.5) + phase;
        }
    }

    private readonly record struct GridRasterization(
        CadViewport Viewport,
        bool IsAliased,
        float MinorScreenStroke,
        float MajorScreenStroke)
    {
        public static GridRasterization Create(
            CadViewport viewport,
            bool isAliased,
            double minorScreenStroke,
            double majorScreenStroke)
        {
            var resolvedMinorStroke = ResolveScreenStroke(minorScreenStroke, isAliased);
            var resolvedMajorStroke = ResolveScreenStroke(majorScreenStroke, isAliased);
            if (isAliased)
            {
                if (majorScreenStroke > minorScreenStroke)
                    resolvedMajorStroke = Math.Max(resolvedMajorStroke, resolvedMinorStroke + 1.0f);
                else if (minorScreenStroke > majorScreenStroke)
                    resolvedMinorStroke = Math.Max(resolvedMinorStroke, resolvedMajorStroke + 1.0f);
            }

            return new GridRasterization(
                viewport,
                isAliased,
                resolvedMinorStroke,
                resolvedMajorStroke);
        }

        public float ResolveWorldStroke(bool major)
        {
            return (major ? MajorScreenStroke : MinorScreenStroke) /
                   (float)Math.Max(Viewport.Zoom, double.Epsilon);
        }

        public CadColor ResolveColor(CadColor color, double configuredStrokeWidth, bool major)
        {
            if (!IsAliased)
                return color;

            var rasterizedStrokeWidth = major ? MajorScreenStroke : MinorScreenStroke;
            var opacityScale = Math.Clamp(
                configuredStrokeWidth / Math.Max(rasterizedStrokeWidth, float.Epsilon),
                0.12,
                1.0);
            var alpha = (byte)Math.Clamp(
                (int)Math.Round(color.A * opacityScale, MidpointRounding.AwayFromZero),
                0,
                byte.MaxValue);
            return CadColor.FromArgb(alpha, color.R, color.G, color.B);
        }

        public double AlignWorldX(double worldX, bool major)
        {
            if (!IsAliased)
                return worldX;

            var screenX = worldX * Viewport.Zoom + Viewport.Offset.X;
            var alignedScreenX = AlignScreenCoordinate(screenX, ResolvePixelSpan(major));
            return (alignedScreenX - Viewport.Offset.X) / Viewport.Zoom;
        }

        public double AlignWorldY(double worldY, bool major)
        {
            if (!IsAliased)
                return worldY;

            var screenY = Viewport.Offset.Y - worldY * Viewport.Zoom;
            var alignedScreenY = AlignScreenCoordinate(screenY, ResolvePixelSpan(major));
            return (Viewport.Offset.Y - alignedScreenY) / Viewport.Zoom;
        }

        public CadPointD AlignDot(CadPointD point, bool major)
        {
            if (!IsAliased)
                return point;

            var pixelSize = ResolveDotPixelSize(major);
            var screen = Viewport.WorldToScreen(point);
            return Viewport.ScreenToWorld(new CadPointD(
                AlignScreenCoordinate(screen.X, pixelSize),
                AlignScreenCoordinate(screen.Y, pixelSize)));
        }

        public double ResolveDotHalfSize(bool major)
        {
            if (IsAliased)
                return ResolveDotPixelSize(major) * 0.5 / Math.Max(Viewport.Zoom, double.Epsilon);

            var factor = major ? 1.7 : 1.25;
            return ResolveWorldStroke(major) * factor;
        }

        private int ResolvePixelSpan(bool major)
        {
            return Math.Max(1, (int)Math.Round(
                major ? MajorScreenStroke : MinorScreenStroke,
                MidpointRounding.AwayFromZero));
        }

        private int ResolveDotPixelSize(bool major)
        {
            if (IsAliased)
            {
                var strokePixels = ResolvePixelSpan(major);
                return major ? strokePixels + 1 : strokePixels;
            }

            var factor = major ? 1.7 : 1.25;
            return Math.Max(1, (int)Math.Round(
                (major ? MajorScreenStroke : MinorScreenStroke) * factor * 2.0,
                MidpointRounding.AwayFromZero));
        }

        private static float ResolveScreenStroke(double configuredWidth, bool isAliased)
        {
            var width = (float)Math.Max(configuredWidth, double.Epsilon);
            return isAliased
                ? Math.Max(1.0f, MathF.Round(width, MidpointRounding.AwayFromZero))
                : width;
        }

        private static double AlignScreenCoordinate(double value, int pixelSpan)
        {
            var phase = (pixelSpan & 1) == 0 ? 0.0 : 0.5;
            return Math.Floor(value - phase + 0.5) + phase;
        }
    }
}
