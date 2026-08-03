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
    private const double MinimumGeometryScreenExtent = 0.5;
    private const double MinimumPaintedGeometryScreenExtent = 0.75;
    private const double ThickStrokeScreenWidth = 1.0;
    private const double PerceptibleStrokeScreenWidth = 0.25;
    private const double MinimumFillScreenThickness = 0.05;
    private const double MinimumFillScreenArea = 0.25;
    private const double MinimumSurfaceScreenArea = 0.25;
    private const double SimplifiedGeometryScreenExtent = 2.0;
    private const double SimplifiedStrokeStyleScreenExtent = 8.0;
    private const double MinimumTextScreenHeight = 0.1;
    private const double MinimumTextScreenExtent = 0.75;
    private const double MinimumTextScreenArea = 0.125;
    private const double TextSummaryScreenHeight = 0.5;
    private const double FullTextScreenHeight = 1.0;
    private const double CompactGeometryProxyScreenExtent = 1.25;
    private const double BlockProxyScreenExtent = 3.0;
    private const double OleProxyMinimumScreenExtent = 8.0;
    private const double LowDetailGeometryScreenExtent = 128.0;
    private const double MediumDetailGeometryScreenExtent = 512.0;

    public static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var screenScale = Math.Max(viewport.Zoom, double.Epsilon) *
                          ResolveTransformScaleMultiplier(options);
        return Resolve(entity, resources, screenScale, options);
    }

    public static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        Matrix3x2 transform,
        CadRenderOptions options,
        float? strokeWidthOverride = null)
    {
        if (entity is CadImage { Opacity: <= 0.0 })
            return Direct2DEntityRenderDetail.Skip;
        if (!options.IsLevelOfDetailEnabled)
            return Direct2DEntityRenderDetail.Full;

        var screenScale = ResolveEffectiveScreenScale(transform, options);
        var screenStrokeWidth = ResolveScreenStrokeWidth(
            resources,
            screenScale,
            options,
            strokeWidthOverride);
        var bounds = entity.Bounds;
        var metricsTransform = transform;
        if (entity is CadImage image)
        {
            bounds = image.FrameBounds;
            metricsTransform = Matrix3x2.CreateRotation(
                                   (float)image.RotationRadians,
                                   new Vector2(
                                       (float)image.FrameBounds.Center.X,
                                       (float)image.FrameBounds.Center.Y)) *
                               transform;
        }

        if (!Direct2DProjectedEntityMetrics.TryCreate(
                bounds,
                metricsTransform,
                ResolveTransformScaleMultiplier(options),
                screenStrokeWidth,
                out var metrics))
        {
            return Direct2DEntityRenderDetail.Full;
        }

        return Resolve(entity, resources, metrics, screenScale);
    }

    private static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        double screenScale,
        CadRenderOptions options,
        float? strokeWidthOverride = null)
    {
        if (entity is CadImage { Opacity: <= 0.0 })
            return Direct2DEntityRenderDetail.Skip;
        if (!options.IsLevelOfDetailEnabled)
            return Direct2DEntityRenderDetail.Full;

        var screenStrokeWidth = ResolveScreenStrokeWidth(
            resources,
            screenScale,
            options,
            strokeWidthOverride);
        Direct2DProjectedEntityMetrics metrics;
        var hasMetrics = entity is CadImage image
            ? Direct2DProjectedEntityMetrics.TryCreate(
                image.FrameBounds,
                Matrix3x2.CreateRotation((float)image.RotationRadians),
                screenScale,
                screenStrokeWidth,
                out metrics)
            : Direct2DProjectedEntityMetrics.TryCreate(
                entity.Bounds,
                screenScale,
                screenStrokeWidth,
                out metrics);
        if (!hasMetrics)
        {
            return Direct2DEntityRenderDetail.Full;
        }

        return Resolve(entity, resources, metrics, screenScale);
    }

    private static Direct2DEntityRenderDetail Resolve(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        Direct2DProjectedEntityMetrics metrics,
        double screenScale)
    {
        if (entity is CadText or CadShapeText)
        {
            return ResolveText(entity, metrics, screenScale) switch
            {
                Direct2DTextRenderDetail.Skip => Direct2DEntityRenderDetail.Skip,
                Direct2DTextRenderDetail.Full => Direct2DEntityRenderDetail.Full,
                _ => Direct2DEntityRenderDetail.Simplified
            };
        }

        if (entity is CadImage)
            return ShouldSkipSurface(metrics)
                ? Direct2DEntityRenderDetail.Skip
                : Direct2DEntityRenderDetail.Full;

        if (ShouldSkipGeometry(resources, metrics))
            return Direct2DEntityRenderDetail.Skip;

        return ShouldUseSimplifiedRendering(entity, metrics)
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    public static Direct2DTextRenderDetail ResolveText(
        CadEntity entity,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        if (!options.IsLevelOfDetailEnabled)
            return Direct2DTextRenderDetail.Full;

        var screenScale = ResolveEffectiveScreenScale(transform, options);
        if (!Direct2DProjectedEntityMetrics.TryCreate(
                entity.Bounds,
                transform,
                ResolveTransformScaleMultiplier(options),
                0.0,
                out var metrics))
        {
            return Direct2DTextRenderDetail.Full;
        }

        return ResolveText(entity, metrics, screenScale);
    }

    public static Direct2DEntityRenderDetail ResolveOle(
        CadRectD bounds,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        if (!options.IsLevelOfDetailEnabled)
            return Direct2DEntityRenderDetail.Full;

        if (bounds.IsEmpty)
            return Direct2DEntityRenderDetail.Full;

        if (!Direct2DProjectedEntityMetrics.TryCreate(
                bounds,
                transform,
                ResolveTransformScaleMultiplier(options),
                0.0,
                out var metrics))
        {
            return Direct2DEntityRenderDetail.Full;
        }

        if (ShouldSkipSurface(metrics))
            return Direct2DEntityRenderDetail.Skip;
        return metrics.MinimumExtent < OleProxyMinimumScreenExtent
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    public static Direct2DEntityRenderDetail ResolveSelection(
        CadEntity entity,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        if (!options.IsLevelOfDetailEnabled)
            return Direct2DEntityRenderDetail.Full;

        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return Direct2DEntityRenderDetail.Full;

        if (!Direct2DProjectedEntityMetrics.TryCreate(
                bounds,
                transform,
                ResolveTransformScaleMultiplier(options),
                0.0,
                out var metrics))
        {
            return Direct2DEntityRenderDetail.Full;
        }

        if (entity is CadText or CadShapeText)
        {
            // Selection is an overlay. A generic rectangle proxy hides the actual
            // glyphs and makes small/rotated text appear to disappear when selected.
            // Keep the same DirectWrite/stroke-font path as the normal renderer so
            // the selection color is the only visual override.
            return Direct2DEntityRenderDetail.Full;
        }

        if (entity is CadImage)
        {
            return ShouldSkipSurface(metrics)
                ? Direct2DEntityRenderDetail.Simplified
                : Direct2DEntityRenderDetail.Full;
        }

        if (metrics.MaximumExtent < MinimumGeometryScreenExtent)
            return Direct2DEntityRenderDetail.Simplified;

        return ShouldUseSimplifiedRendering(entity, metrics)
            ? Direct2DEntityRenderDetail.Simplified
            : Direct2DEntityRenderDetail.Full;
    }

    private static bool ShouldUseSimplifiedRendering(
        CadEntity entity,
        Direct2DProjectedEntityMetrics metrics)
    {
        if (entity is not (CadImage or CadOleObject or CadText or CadShapeText) &&
            metrics.MaximumExtent < CompactGeometryProxyScreenExtent &&
            metrics.ScreenStrokeWidth < ThickStrokeScreenWidth)
        {
            return true;
        }

        return entity switch
        {
            CadSpline => metrics.MaximumExtent < SimplifiedGeometryScreenExtent,
            CadPolyline polyline when polyline.Points.Count > 8 =>
                metrics.MaximumExtent < SimplifiedGeometryScreenExtent,
            CadBlockReference => metrics.MaximumExtent < BlockProxyScreenExtent,
            CadOleObject => metrics.MinimumExtent < OleProxyMinimumScreenExtent,
            _ => false
        };
    }

    public static bool ShouldSimplifyStrokeStyle(
        CadEntity entity,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        if (!options.IsLevelOfDetailEnabled ||
            entity.Bounds.IsEmpty)
        {
            return false;
        }

        return Direct2DProjectedEntityMetrics.TryCreate(
                   entity.Bounds,
                   transform,
                   ResolveTransformScaleMultiplier(options),
                   0.0,
                   out var metrics) &&
               metrics.MaximumExtent < SimplifiedStrokeStyleScreenExtent;
    }

    public static double ResolveMaximumScreenScale(Matrix3x2 transform)
    {
        var scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = Math.Max(scaleX, scaleY);
        return double.IsFinite(scale) && scale > double.Epsilon ? scale : 1.0;
    }

    public static double ResolveEffectiveScreenScale(
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        return ResolveMaximumScreenScale(transform) *
               ResolveTransformScaleMultiplier(options);
    }

    public static ID2D1Geometry? ResolveGeometry(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        var geometry = resources.Geometry;
        if (!options.IsLevelOfDetailEnabled ||
            geometry is null ||
            entity is not (CadPolyline or CadSpline))
        {
            return geometry;
        }

        if (!Direct2DProjectedEntityMetrics.TryCreate(
                entity.Bounds,
                transform,
                ResolveTransformScaleMultiplier(options),
                0.0,
                out var metrics))
        {
            return geometry;
        }

        if (metrics.MaximumExtent <= LowDetailGeometryScreenExtent &&
            resources.LowDetailGeometry is not null)
        {
            return resources.LowDetailGeometry;
        }

        return metrics.MaximumExtent <= MediumDetailGeometryScreenExtent &&
               resources.MediumDetailGeometry is not null
            ? resources.MediumDetailGeometry
            : geometry;
    }

    private static double ResolveScreenStrokeWidth(
        Direct2DResourceCache.EntityResourceBucket? resources,
        double zoom,
        CadRenderOptions options,
        float? strokeWidthOverride)
    {
        if (resources?.StrokeBrush is null)
            return 0;

        var strokeWidth = strokeWidthOverride ?? resources.StrokeWidth;
        var screenWidth = options.KeepStrokeWidthScreenConstant
            ? strokeWidth
            : strokeWidth * zoom;
        return Math.Max(screenWidth, options.MinimumScreenStrokeWidth);
    }

    private static Direct2DTextRenderDetail ResolveText(
        CadEntity entity,
        Direct2DProjectedEntityMetrics metrics,
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
        if (metrics.MaximumExtent < MinimumTextScreenExtent ||
            screenHeight < MinimumTextScreenHeight &&
            metrics.ProjectedArea < MinimumTextScreenArea)
        {
            return Direct2DTextRenderDetail.Skip;
        }
        if (screenHeight < TextSummaryScreenHeight)
            return Direct2DTextRenderDetail.Baseline;
        return screenHeight < FullTextScreenHeight
            ? Direct2DTextRenderDetail.Summary
            : Direct2DTextRenderDetail.Full;
    }

    private static bool ShouldSkipGeometry(
        Direct2DResourceCache.EntityResourceBucket? resources,
        Direct2DProjectedEntityMetrics metrics)
    {
        if (metrics.MaximumExtent + metrics.ScreenStrokeWidth <
            MinimumPaintedGeometryScreenExtent)
        {
            return true;
        }

        var hasFill = resources is { FillBrush: not null } or { HatchBrush: not null };
        if (!hasFill)
            return false;

        var hasPerceptibleStroke = resources?.StrokeBrush is not null &&
                                   metrics.MaximumExtent >= MinimumGeometryScreenExtent &&
                                   metrics.ScreenStrokeWidth >= PerceptibleStrokeScreenWidth;
        return !hasPerceptibleStroke &&
               metrics.MinimumExtent < MinimumFillScreenThickness &&
               metrics.ProjectedArea < MinimumFillScreenArea;
    }

    private static bool ShouldSkipSurface(Direct2DProjectedEntityMetrics metrics)
    {
        return metrics.MaximumExtent < MinimumGeometryScreenExtent ||
               (metrics.MinimumExtent < MinimumFillScreenThickness &&
                metrics.ProjectedArea < MinimumSurfaceScreenArea);
    }

    private static double ResolveTransformScaleMultiplier(CadRenderOptions options)
    {
        var multiplier = options.TransformScaleMultiplier;
        return double.IsFinite(multiplier) && multiplier > double.Epsilon
            ? multiplier
            : 1.0;
    }
}
