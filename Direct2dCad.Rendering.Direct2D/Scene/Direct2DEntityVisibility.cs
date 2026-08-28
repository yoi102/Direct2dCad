using System.Diagnostics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal static class Direct2DEntityVisibility
{
    private const int MinimumOrderedScanEntityCount = 256;
    private const int OrderedScanCandidateDivisor = 8;
    private const double DefaultBroadPhasePaddingPixels = 64.0;
    private const double Direct2DDefaultMiterLimit = 10.0;

    public static IEnumerable<Direct2DVisibleEntity> Enumerate(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Direct2DOwnerRenderPacket renderPacket,
        List<EntityId> candidateIdBuffer,
        HashSet<EntityId> candidateSetBuffer,
        List<int> candidateIndexBuffer,
        Direct2DRenderStatisticsCollector? statistics = null)
    {
        if (renderPacket.Entries.Count == 0)
            return [];

        var renderWorldBounds = ResolveRenderWorldBounds(viewport, options);
        if (renderWorldBounds is not { } bounds ||
            options.EntityBoundsQueryInto is null &&
            options.EntityBoundsQuery is null)
        {
            return EnumeratePacketSubset(
                viewport,
                options,
                resourceCache,
                renderPacket.Entries,
                renderWorldBounds);
        }

        var broadPhasePadding = ResolveBroadPhasePadding(
            resourceCache,
            viewport,
            options);
        var queryBounds = bounds.Inflate(broadPhasePadding);
        if (!renderPacket.Bounds.IsEmpty &&
            queryBounds.Contains(renderPacket.Bounds))
        {
            return EnumeratePacketSubset(
                viewport,
                options,
                resourceCache,
                renderPacket.Entries,
                bounds);
        }

        IReadOnlyList<EntityId> candidateIds;
        var queryStarted = Stopwatch.GetTimestamp();
        try
        {
            if (options.EntityBoundsQueryInto is { } bufferedQuery)
            {
                candidateIdBuffer.Clear();
                bufferedQuery(
                    options.ActiveOwnerBlockId,
                    queryBounds,
                    candidateIdBuffer);
                candidateIds = candidateIdBuffer;
            }
            else
            {
                candidateIds = options.EntityBoundsQuery!(
                    options.ActiveOwnerBlockId,
                    queryBounds);
            }
        }
        finally
        {
            statistics?.RecordVisibilityQuery(
                Stopwatch.GetElapsedTime(queryStarted).TotalMilliseconds);
        }

        if (candidateIds.Count >= renderPacket.Entries.Count / 2)
        {
            return EnumeratePacketSubset(
                viewport,
                options,
                resourceCache,
                renderPacket.Entries,
                bounds);
        }

        candidateIndexBuffer.Clear();
        if (candidateIndexBuffer.Capacity < candidateIds.Count)
            candidateIndexBuffer.Capacity = candidateIds.Count;

        if (renderPacket.Entries.Count >= MinimumOrderedScanEntityCount &&
            candidateIds.Count >=
            renderPacket.Entries.Count / OrderedScanCandidateDivisor)
        {
            candidateSetBuffer.Clear();
            foreach (var entityId in candidateIds)
                candidateSetBuffer.Add(entityId);
            for (var index = 0; index < renderPacket.Entries.Count; index++)
            {
                if (candidateSetBuffer.Contains(
                        renderPacket.Entries[index].Entity.Id))
                {
                    candidateIndexBuffer.Add(index);
                }
            }
        }
        else
        {
            foreach (var entityId in candidateIds)
            {
                if (renderPacket.TryGetIndex(entityId, out var index))
                    candidateIndexBuffer.Add(index);
            }

            var sortingStarted = Stopwatch.GetTimestamp();
            try
            {
                candidateIndexBuffer.Sort();
            }
            finally
            {
                statistics?.RecordCandidateSorting(
                    Stopwatch.GetElapsedTime(sortingStarted).TotalMilliseconds);
            }
        }

        return EnumeratePacketIndices(
            viewport,
            options,
            resourceCache,
            renderPacket.Entries,
            candidateIndexBuffer,
            bounds);
    }

    public static IEnumerable<Direct2DVisibleEntity> Enumerate(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<CadEntity> orderedEntities,
        Direct2DEntityOrderCache entityOrderCache,
        List<EntityId>? candidateIdBuffer = null,
        HashSet<EntityId>? candidateSetBuffer = null,
        List<CadEntity>? candidateEntityBuffer = null,
        List<Direct2DEntityOrderCache.RankedEntity>? rankedEntityBuffer = null,
        Direct2DRenderStatisticsCollector? statistics = null)
    {
        if (orderedEntities.Count == 0)
            return [];

        var renderWorldBounds = ResolveRenderWorldBounds(viewport, options);
        var canUseBufferedQuery = candidateIdBuffer is not null &&
                                  options.EntityBoundsQueryInto is not null;
        if (renderWorldBounds is not { } bounds ||
            !canUseBufferedQuery && options.EntityBoundsQuery is null)
        {
            return EnumerateOrderedSubset(
                document,
                viewport,
                options,
                resourceCache,
                orderedEntities,
                renderWorldBounds);
        }

        var broadPhasePadding = ResolveBroadPhasePadding(
            resourceCache,
            viewport,
            options);
        var queryBounds = bounds.Inflate(broadPhasePadding);
        var ownerBounds = entityOrderCache.GetOwnerBounds(
            document,
            options.ActiveOwnerBlockId);
        if (!ownerBounds.IsEmpty && queryBounds.Contains(ownerBounds))
        {
            return EnumerateOrderedSubset(
                document,
                viewport,
                options,
                resourceCache,
                orderedEntities,
                bounds);
        }

        IReadOnlyList<EntityId> candidateIds;
        var queryStarted = Stopwatch.GetTimestamp();
        try
        {
            if (canUseBufferedQuery)
            {
                candidateIdBuffer!.Clear();
                options.EntityBoundsQueryInto!(
                    options.ActiveOwnerBlockId,
                    queryBounds,
                    candidateIdBuffer);
                candidateIds = candidateIdBuffer;
            }
            else
            {
                candidateIds = options.EntityBoundsQuery!(options.ActiveOwnerBlockId, queryBounds);
            }
        }
        finally
        {
            statistics?.RecordVisibilityQuery(
                Stopwatch.GetElapsedTime(queryStarted).TotalMilliseconds);
        }
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
            var candidateSet = candidateSetBuffer ?? new HashSet<EntityId>();
            candidateSet.Clear();
            foreach (var entityId in candidateIds)
                candidateSet.Add(entityId);

            var orderedCandidates = PrepareEntityBuffer(
                candidateEntityBuffer,
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

        var candidates = PrepareEntityBuffer(candidateEntityBuffer, candidateIds.Count);
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

        var sortingStarted = Stopwatch.GetTimestamp();
        try
        {
            entityOrderCache.SortCandidates(
                document,
                options.ActiveOwnerBlockId,
                candidates,
                rankedEntityBuffer ?? []);
        }
        finally
        {
            statistics?.RecordCandidateSorting(
                Stopwatch.GetElapsedTime(sortingStarted).TotalMilliseconds);
        }
        return EnumerateOrderedSubset(
            document,
            viewport,
            options,
            resourceCache,
            candidates,
            bounds);
    }

    private static List<CadEntity> PrepareEntityBuffer(
        List<CadEntity>? buffer,
        int capacity)
    {
        var result = buffer ?? new List<CadEntity>(capacity);
        result.Clear();
        if (result.Capacity < capacity)
            result.Capacity = capacity;
        return result;
    }

    private static IEnumerable<Direct2DVisibleEntity> EnumeratePacketSubset(
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<Direct2DEntityRenderPacket> entries,
        CadRectD? renderWorldBounds)
    {
        var broadPhasePadding = ResolveBroadPhasePadding(
            resourceCache,
            viewport,
            options);
        foreach (var entry in entries)
        {
            if (TryResolveVisiblePacket(
                    viewport,
                    options,
                    resourceCache,
                    entry,
                    renderWorldBounds,
                    broadPhasePadding,
                    out var visible))
            {
                yield return visible;
            }
        }
    }

    private static IEnumerable<Direct2DVisibleEntity> EnumeratePacketIndices(
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<Direct2DEntityRenderPacket> entries,
        IReadOnlyList<int> indices,
        CadRectD? renderWorldBounds)
    {
        var broadPhasePadding = ResolveBroadPhasePadding(
            resourceCache,
            viewport,
            options);
        foreach (var index in indices)
        {
            if ((uint)index >= (uint)entries.Count)
                continue;
            if (TryResolveVisiblePacket(
                    viewport,
                    options,
                    resourceCache,
                    entries[index],
                    renderWorldBounds,
                    broadPhasePadding,
                    out var visible))
            {
                yield return visible;
            }
        }
    }

    private static bool TryResolveVisiblePacket(
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Direct2DEntityRenderPacket entry,
        CadRectD? renderWorldBounds,
        double broadPhasePadding,
        out Direct2DVisibleEntity visible)
    {
        var entity = entry.Entity;
        if (!entry.IsRenderable ||
            options.HiddenEntityIds.Contains(entity.Id) ||
            renderWorldBounds is { } coarseBounds &&
            !MayIntersectRenderBounds(
                entry.Bounds,
                coarseBounds,
                broadPhasePadding))
        {
            visible = default;
            return false;
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
                resources,
                broadPhasePadding))
        {
            visible = default;
            return false;
        }

        visible = new Direct2DVisibleEntity(entity, resources);
        return true;
    }

    internal static bool TryResolveVisibleEntity(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        CadEntity entity,
        CadRectD? renderWorldBounds,
        out Direct2DVisibleEntity visible)
    {
        if (entity.IsErased ||
            !entity.IsVisible ||
            options.HiddenEntityIds.Contains(entity.Id) ||
            !document.TryGetLayer(entity.LayerId, out var layer) ||
            layer is not { IsVisible: true, IsFrozen: false })
        {
            visible = default;
            return false;
        }

        var broadPhasePadding = ResolveBroadPhasePadding(resourceCache, viewport, options);
        if (renderWorldBounds is { } coarseBounds &&
            !MayIntersectRenderBounds(entity, coarseBounds, broadPhasePadding))
        {
            visible = default;
            return false;
        }

        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        if (Direct2DEntityLevelOfDetail.Resolve(entity, resources, viewport, options) ==
                Direct2DEntityRenderDetail.Skip ||
            renderWorldBounds is { } bounds &&
            !IntersectsRenderBounds(
                entity,
                bounds,
                viewport,
                options,
                resources,
                broadPhasePadding))
        {
            visible = default;
            return false;
        }

        visible = new Direct2DVisibleEntity(entity, resources);
        return true;
    }

    internal static IEnumerable<Direct2DVisibleEntity> EnumerateOrderedSubset(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        IReadOnlyList<CadEntity> entities,
        CadRectD? renderWorldBounds)
    {
        foreach (var entity in entities)
        {
            if (TryResolveVisibleEntity(
                    document,
                    viewport,
                    options,
                    resourceCache,
                    entity,
                    renderWorldBounds,
                    out var visible))
            {
                yield return visible;
            }
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
        Direct2DResourceCache.EntityResourceBucket? resources,
        double broadPhasePadding)
    {
        var bounds = ResolvePaintBounds(
            entity,
            resources,
            viewport,
            options,
            broadPhasePadding);
        return bounds.Intersects(renderWorldBounds) ||
               bounds.Contains(renderWorldBounds.Center) ||
               renderWorldBounds.Contains(bounds);
    }

    private static bool MayIntersectRenderBounds(
        CadEntity entity,
        CadRectD renderWorldBounds,
        double broadPhasePadding)
    {
        return MayIntersectRenderBounds(
            entity.Bounds,
            renderWorldBounds,
            broadPhasePadding);
    }

    private static bool MayIntersectRenderBounds(
        CadRectD entityBounds,
        CadRectD renderWorldBounds,
        double broadPhasePadding)
    {
        if (entityBounds.IsEmpty)
            return true;

        var paddedBounds = entityBounds.Inflate(broadPhasePadding);
        return paddedBounds.Intersects(renderWorldBounds) ||
               paddedBounds.Contains(renderWorldBounds.Center) ||
               renderWorldBounds.Contains(paddedBounds);
    }

    private static CadRectD ResolvePaintBounds(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadRenderOptions options,
        double broadPhasePadding)
    {
        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return bounds;

        var padding = 0.0;
        if (resources?.StrokeBrush is not null && UsesStrokeWidth(entity))
        {
            padding = Math.Max(
                padding,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options) *
                ResolveStrokeExtentMultiplier(entity));
        }

        if (resources is { FillBrush: not null } or { HatchBrush: not null })
        {
            padding = Math.Max(
                padding,
                Math.Max(options.MinimumScreenStrokeWidth, 2.0) /
                Math.Max(viewport.Zoom, double.Epsilon));
        }

        if (entity is CadBlockReference)
            padding = Math.Max(padding, broadPhasePadding);

        return padding > 0 ? bounds.Inflate(padding) : bounds;
    }

    internal static double ResolveBroadPhasePadding(
        Direct2DResourceCache resourceCache,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var minimumPadding = DefaultBroadPhasePaddingPixels / zoom;
        var maximumStrokeWidth = Math.Max(
            resourceCache.MaximumStrokeWidth,
            0.0f);
        var maximumWorldStrokeWidth = options.KeepStrokeWidthScreenConstant
            ? CadLineWeightDisplay.ToDipsSingle(maximumStrokeWidth) / zoom
            : maximumStrokeWidth * Direct2DEntityRenderer.ResolveEntityLineWeightWorldScale(options);
        maximumWorldStrokeWidth = Math.Max(
            maximumWorldStrokeWidth,
            Math.Max(options.MinimumScreenStrokeWidth, 0.0) / zoom);
        return Math.Max(
            minimumPadding,
            maximumWorldStrokeWidth * (Direct2DDefaultMiterLimit * 0.5));
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
            CadCompositePath or
            CadShapeText;
    }

    private static double ResolveStrokeExtentMultiplier(CadEntity entity)
    {
        var multiplier = 0.5;
        if (entity is CadShapeText ||
            CadEntityCapabilities.SupportsLineJoin(entity) &&
            entity.StrokeStyle.LineJoin is
                CadStrokeLineJoin.Miter or CadStrokeLineJoin.MiterOrBevel)
        {
            multiplier = Direct2DDefaultMiterLimit * 0.5;
        }

        if (CadEntityCapabilities.SupportsStartEndCaps(entity) &&
            (entity.StrokeStyle.StartCap == CadStrokeCap.Triangle ||
             entity.StrokeStyle.EndCap == CadStrokeCap.Triangle ||
             entity.StrokeStyle.DashCap == CadStrokeCap.Triangle))
        {
            multiplier = Math.Max(multiplier, 1.0);
        }

        return multiplier;
    }

    private static float ResolveStrokeWidth(
        float modelStrokeWidth,
        CadViewport viewport,
        CadRenderOptions options)
    {
        return Direct2DEntityRenderer.ResolveStrokeWidth(modelStrokeWidth, viewport, options);
    }
}

internal readonly record struct Direct2DVisibleEntity(
    CadEntity Entity,
    Direct2DResourceCache.EntityResourceBucket? Resources);
