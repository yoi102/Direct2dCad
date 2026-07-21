using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal static class Direct2DEntityVisibility
{
    private const int MinimumOrderedScanEntityCount = 256;
    private const int OrderedScanCandidateDivisor = 8;

    public static IEnumerable<CadEntity> Enumerate(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<CadEntity> orderedEntities,
        Direct2DEntityOrderCache entityOrderCache)
    {
        if (orderedEntities.Count == 0)
            return [];

        var renderWorldBounds = ResolveRenderWorldBounds(viewport, options);
        if (renderWorldBounds is not { } bounds ||
            options.EntityBoundsQuery is not { } query)
        {
            return EnumerateOrderedSubset(
                document,
                viewport,
                options,
                resourceCache,
                orderedEntities,
                renderWorldBounds);
        }

        var broadPhasePadding = 64.0 / Math.Max(viewport.Zoom, double.Epsilon);
        var candidateIds = query(bounds.Inflate(broadPhasePadding));
        if (candidateIds.Count > 0 &&
            candidateIds.Count >= orderedEntities.Count / 2)
        {
            return EnumerateOrderedSubset(
                document,
                viewport,
                options,
                resourceCache,
                orderedEntities,
                bounds);
        }

        if (orderedEntities.Count >= MinimumOrderedScanEntityCount &&
            candidateIds.Count >= orderedEntities.Count / OrderedScanCandidateDivisor)
        {
            var candidateSet = candidateIds.ToHashSet();
            var orderedCandidates = new List<CadEntity>(
                Math.Min(candidateSet.Count, orderedEntities.Count));
            foreach (var entity in orderedEntities)
            {
                if (candidateSet.Contains(entity.Id))
                    orderedCandidates.Add(entity);
            }

            return EnumerateOrderedSubset(
                document,
                viewport,
                options,
                resourceCache,
                orderedCandidates,
                bounds);
        }

        var candidates = new List<CadEntity>(candidateIds.Count);
        foreach (var entityId in candidateIds)
        {
            if (!document.TryGetEntity(entityId, out var entity) ||
                entity is null ||
                !entity.OwnerBlockId.Equals(options.ActiveOwnerBlockId))
            {
                continue;
            }

            candidates.Add(entity);
        }

        candidates.Sort(entityOrderCache.GetComparer(
            document,
            options.ActiveOwnerBlockId));
        return EnumerateOrderedSubset(
            document,
            viewport,
            options,
            resourceCache,
            candidates,
            bounds);
    }

    internal static IEnumerable<CadEntity> EnumerateOrderedSubset(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<CadEntity> entities,
        CadRectD? renderWorldBounds)
    {
        foreach (var entity in entities)
        {
            if (entity.IsErased ||
                !entity.IsVisible ||
                options.HiddenEntityIds.Contains(entity.Id) ||
                !document.TryGetLayer(entity.LayerId, out var layer) ||
                layer is not { IsVisible: true, IsFrozen: false })
            {
                continue;
            }

            resourceCache.TryGetEntityResources(entity.Id, out var resources);
            if (Direct2DEntityLevelOfDetail.Resolve(
                    entity,
                    resources,
                    viewport,
                    options) == Direct2DEntityRenderDetail.Skip ||
                renderWorldBounds is { } bounds &&
                !IntersectsRenderBounds(
                    entity,
                    bounds,
                    viewport,
                    options,
                    resources))
            {
                continue;
            }

            yield return entity;
        }
    }

    internal static CadRectD? ResolveRenderWorldBounds(
        CadViewport viewport,
        CadRenderOptions options)
    {
        var padding = Math.Max(
            options.MinimumScreenStrokeWidth,
            options.KeepStrokeWidthScreenConstant ? 6.0 : 2.0) /
            Math.Max(viewport.Zoom, double.Epsilon);
        if (options.DirtyWorldBounds is { IsEmpty: false } dirty)
            return dirty.Inflate(padding);

        return viewport.VisibleWorldBounds.IsEmpty
            ? null
            : viewport.VisibleWorldBounds.Inflate(padding);
    }

    private static bool IntersectsRenderBounds(
        CadEntity entity,
        CadRectD renderWorldBounds,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache.EntityResourceBucket? resources)
    {
        var broadPhasePadding = 64.0 / Math.Max(viewport.Zoom, double.Epsilon);
        var entityBounds = entity.Bounds;
        if (!entityBounds.IsEmpty &&
            !entityBounds.Inflate(broadPhasePadding).Intersects(renderWorldBounds))
        {
            return false;
        }

        var bounds = ResolvePaintBounds(entity, resources, viewport, options);
        return bounds.Intersects(renderWorldBounds) ||
               bounds.Contains(renderWorldBounds.Center) ||
               renderWorldBounds.Contains(bounds);
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

        if (entity is CadBlockReference)
        {
            padding = Math.Max(
                padding,
                Math.Max(options.MinimumScreenStrokeWidth, 8.0) /
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
            CadShapeText;
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
