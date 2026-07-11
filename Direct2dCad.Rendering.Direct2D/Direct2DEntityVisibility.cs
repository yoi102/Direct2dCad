using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D;

internal static class Direct2DEntityVisibility
{
    public static IEnumerable<CadEntity> Enumerate(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache)
    {
        var dirtyWorldBounds = ResolveDirtyWorldBounds(viewport, options);
        return document.Entities.Values
            .Where(entity =>
                !entity.IsErased &&
                entity.IsVisible &&
                !options.HiddenEntityIds.Contains(entity.Id) &&
                (dirtyWorldBounds is null || IntersectsDirtyBounds(
                    entity,
                    dirtyWorldBounds.Value,
                    viewport,
                    options,
                    resourceCache)) &&
                document.TryGetLayer(entity.LayerId, out var layer) &&
                layer is { IsVisible: true, IsFrozen: false })
            .OrderBy(entity => document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenBy(entity => entity.ZIndex)
            .ThenBy(entity => entity.Id.Value);
    }

    private static CadRectD? ResolveDirtyWorldBounds(CadViewport viewport, CadRenderOptions options)
    {
        if (options.DirtyWorldBounds is not { } dirty || dirty.IsEmpty)
            return null;

        var padding = Math.Max(
            options.MinimumScreenStrokeWidth,
            options.KeepStrokeWidthScreenConstant ? 6.0 : 2.0) /
            Math.Max(viewport.Zoom, double.Epsilon);
        return dirty.Inflate(padding);
    }

    private static bool IntersectsDirtyBounds(
        CadEntity entity,
        CadRectD dirtyWorldBounds,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache)
    {
        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        var bounds = ResolvePaintBounds(entity, resources, viewport, options);
        return bounds.Intersects(dirtyWorldBounds) ||
               bounds.Contains(dirtyWorldBounds.Center) ||
               dirtyWorldBounds.Contains(bounds);
    }

    private static CadRectD ResolvePaintBounds(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return bounds;

        var padding = 0.0;
        if (resources?.StrokeBrush is not null && UsesStrokeWidth(entity))
            padding = Math.Max(padding, ResolveStrokeWidth(resources.StrokeWidth, viewport, options) * 0.5);

        if (resources is { FillBrush: not null } or { HatchBrush: not null })
        {
            padding = Math.Max(
                padding,
                Math.Max(options.MinimumScreenStrokeWidth, 2.0) /
                Math.Max(viewport.Zoom, double.Epsilon));
        }

        return padding > 0 ? bounds.Inflate(padding) : bounds;
    }

    private static bool UsesStrokeWidth(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadEllipseArc or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadShapeText or
            CadBlockReference;
    }

    private static float ResolveStrokeWidth(
        float modelStrokeWidth,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var zoom = Math.Max((float)viewport.Zoom, float.Epsilon);
        var strokeWidth = options.KeepStrokeWidthScreenConstant
            ? modelStrokeWidth / zoom
            : modelStrokeWidth;
        return Math.Max(strokeWidth, (float)options.MinimumScreenStrokeWidth / zoom);
    }
}
