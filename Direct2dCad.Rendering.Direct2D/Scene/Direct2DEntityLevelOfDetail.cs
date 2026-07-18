using System.Numerics;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal enum Direct2DEntityRenderDetail
{
    Skip,
    Simplified,
    Full
}

internal enum Direct2DTextRenderDetail
{
    Skip,
    Baseline,
    Summary,
    Full
}

internal static class Direct2DEntityLevelOfDetail
{
    private const double MinimumGeometryScreenExtent = 0.35;
    private const double ThickStrokeScreenWidth = 1.0;
    private const double SimplifiedGeometryScreenExtent = 2.0;
    private const double SimplifiedStrokeStyleScreenExtent = 8.0;
    private const double MinimumTextScreenHeight = 0.35;
    private const double TextSummaryScreenHeight = 1.0;
    private const double FullTextScreenHeight = 2.5;
    private const double BlockProxyScreenExtent = 12.0;
    private const double OleProxyMinimumScreenExtent = 8.0;
    private const double LowDetailGeometryScreenExtent = 128.0;
    private const double MediumDetailGeometryScreenExtent = 512.0;

    public static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        return Resolve(entity, resources, Math.Max(viewport.Zoom, double.Epsilon), options);
    }

    public static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        return Resolve(entity, resources, ResolveMaximumScreenScale(transform), options);
    }

    private static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        double screenScale,
        CadRenderOptions options)
    {
        if (entity is CadText or CadShapeText)
        {
            return ResolveText(entity, screenScale) switch
            {
                Direct2DTextRenderDetail.Skip => Direct2DEntityRenderDetail.Skip,
                Direct2DTextRenderDetail.Full => Direct2DEntityRenderDetail.Full,
                _ => Direct2DEntityRenderDetail.Simplified
            };
        }

        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return Direct2DEntityRenderDetail.Full;

        var screenWidth = Math.Abs(bounds.Width) * screenScale;
        var screenHeight = Math.Abs(bounds.Height) * screenScale;
        var maximumExtent = Math.Max(screenWidth, screenHeight);
        if (!double.IsFinite(maximumExtent))
            return Direct2DEntityRenderDetail.Full;

        if (maximumExtent < MinimumGeometryScreenExtent &&
            ResolveScreenStrokeWidth(resources, screenScale, options) < ThickStrokeScreenWidth)
        {
            return Direct2DEntityRenderDetail.Skip;
        }

        return ShouldUseSimplifiedRendering(entity, screenWidth, screenHeight)
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    public static Direct2DTextRenderDetail ResolveText(
        CadEntity entity,
        Matrix3x2 transform)
    {
        return ResolveText(entity, ResolveMaximumScreenScale(transform));
    }

    public static Direct2DEntityRenderDetail ResolveOle(
        CadRectD bounds,
        Matrix3x2 transform)
    {
        if (bounds.IsEmpty)
            return Direct2DEntityRenderDetail.Full;

        var screenScale = ResolveMaximumScreenScale(transform);
        var screenWidth = Math.Abs(bounds.Width) * screenScale;
        var screenHeight = Math.Abs(bounds.Height) * screenScale;
        var maximumExtent = Math.Max(screenWidth, screenHeight);
        if (!double.IsFinite(maximumExtent))
            return Direct2DEntityRenderDetail.Full;
        if (maximumExtent < MinimumGeometryScreenExtent)
            return Direct2DEntityRenderDetail.Skip;
        return Math.Min(screenWidth, screenHeight) < OleProxyMinimumScreenExtent
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    public static Direct2DEntityRenderDetail ResolveSelection(
        CadEntity entity,
        Matrix3x2 transform)
    {
        if (entity is CadText or CadShapeText)
        {
            return ResolveText(entity, transform) switch
            {
                Direct2DTextRenderDetail.Skip => Direct2DEntityRenderDetail.Skip,
                Direct2DTextRenderDetail.Full => Direct2DEntityRenderDetail.Full,
                _ => Direct2DEntityRenderDetail.Simplified
            };
        }

        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return Direct2DEntityRenderDetail.Full;

        var screenScale = ResolveMaximumScreenScale(transform);
        var screenWidth = Math.Abs(bounds.Width) * screenScale;
        var screenHeight = Math.Abs(bounds.Height) * screenScale;
        var maximumExtent = Math.Max(screenWidth, screenHeight);
        if (!double.IsFinite(maximumExtent))
            return Direct2DEntityRenderDetail.Full;
        if (maximumExtent < MinimumGeometryScreenExtent)
            return Direct2DEntityRenderDetail.Skip;

        return ShouldUseSimplifiedRendering(entity, screenWidth, screenHeight)
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    private static bool ShouldUseSimplifiedRendering(
        CadEntity entity,
        double screenWidth,
        double screenHeight)
    {
        var maximumExtent = Math.Max(screenWidth, screenHeight);
        return entity switch
        {
            CadImage => Math.Min(screenWidth, screenHeight) < SimplifiedGeometryScreenExtent,
            CadSpline => maximumExtent < SimplifiedGeometryScreenExtent,
            CadPolyline polyline when polyline.Points.Count > 8 =>
                maximumExtent < SimplifiedGeometryScreenExtent,
            CadBlockReference => maximumExtent < BlockProxyScreenExtent,
            CadOleObject => Math.Min(screenWidth, screenHeight) < OleProxyMinimumScreenExtent,
            _ => false
        };
    }

    public static ID2D1StrokeStyle? ResolveStrokeStyle(
        CadEntity entity,
        ID2D1StrokeStyle? strokeStyle,
        Matrix3x2 transform)
    {
        if (strokeStyle is null || entity.Bounds.IsEmpty)
            return strokeStyle;

        var screenScale = ResolveMaximumScreenScale(transform);
        var maximumExtent = Math.Max(
            Math.Abs(entity.Bounds.Width) * screenScale,
            Math.Abs(entity.Bounds.Height) * screenScale);
        return double.IsFinite(maximumExtent) &&
               maximumExtent < SimplifiedStrokeStyleScreenExtent
            ? null
            : strokeStyle;
    }

    public static double ResolveMaximumScreenScale(Matrix3x2 transform)
    {
        var scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = Math.Max(scaleX, scaleY);
        return double.IsFinite(scale) && scale > double.Epsilon ? scale : 1.0;
    }

    public static ID2D1Geometry? ResolveGeometry(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        Matrix3x2 transform)
    {
        var geometry = resources.Geometry;
        if (geometry is null || entity is not (CadPolyline or CadSpline))
            return geometry;

        var bounds = entity.Bounds;
        var maximumScreenExtent = Math.Max(bounds.Width, bounds.Height) *
                                  ResolveMaximumScreenScale(transform);
        if (!double.IsFinite(maximumScreenExtent))
            return geometry;
        if (maximumScreenExtent <= LowDetailGeometryScreenExtent &&
            resources.LowDetailGeometry is not null)
        {
            return resources.LowDetailGeometry;
        }

        return maximumScreenExtent <= MediumDetailGeometryScreenExtent &&
               resources.MediumDetailGeometry is not null
            ? resources.MediumDetailGeometry
            : geometry;
    }

    private static double ResolveScreenStrokeWidth(
        Direct2DResourceCache.EntityResourceBucket? resources,
        double zoom,
        CadRenderOptions options)
    {
        if (resources?.StrokeBrush is null)
            return 0;

        var screenWidth = options.KeepStrokeWidthScreenConstant
            ? resources.StrokeWidth
            : resources.StrokeWidth * zoom;
        return Math.Max(screenWidth, options.MinimumScreenStrokeWidth);
    }

    private static Direct2DTextRenderDetail ResolveText(
        CadEntity entity,
        double screenScale)
    {
        var modelHeight = entity switch
        {
            CadText text => text.Height,
            CadShapeText shapeText => shapeText.Height,
            _ => double.PositiveInfinity
        };
        var screenHeight = Math.Abs(modelHeight) * screenScale;
        if (!double.IsFinite(screenHeight))
            return Direct2DTextRenderDetail.Full;
        if (screenHeight < MinimumTextScreenHeight)
            return Direct2DTextRenderDetail.Skip;
        if (screenHeight < TextSummaryScreenHeight)
            return Direct2DTextRenderDetail.Baseline;
        return screenHeight < FullTextScreenHeight
            ? Direct2DTextRenderDetail.Summary
            : Direct2DTextRenderDetail.Full;
    }
}
