using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal static class Direct2DBlockEntityVisibility
{
    private const int MinimumIndexedEntityCount = 256;
    private const int OrderedScanCandidateDivisor = 8;
    private const double PaintPaddingPixels = 64.0;

    public static IReadOnlyList<CadEntity> Resolve(
        CadDocument document,
        BlockId ownerBlockId,
        Matrix3x2 localToScreen,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DEntityOrderCache entityOrderCache,
        List<EntityId> candidateIdsBuffer,
        List<CadEntity> candidatesBuffer,
        List<CadEntity> orderedCandidatesBuffer,
        HashSet<EntityId> candidateSetBuffer,
        List<Direct2DEntityOrderCache.RankedEntity> rankedEntityBuffer)
    {
        var orderedEntities = entityOrderCache.GetOrderedEntities(document, ownerBlockId);
        if (orderedEntities.Count < MinimumIndexedEntityCount ||
            options.EntityBoundsQueryInto is null && options.EntityBoundsQuery is null ||
            !TryResolveVisibleLocalBounds(localToScreen, viewport, out var visibleBounds))
        {
            return orderedEntities;
        }

        var screenScale = Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(localToScreen);
        var queryBounds = visibleBounds.Inflate(PaintPaddingPixels / screenScale);
        var ownerBounds = entityOrderCache.GetOwnerBounds(document, ownerBlockId);
        if (!ownerBounds.IsEmpty && queryBounds.Contains(ownerBounds))
            return orderedEntities;

        IReadOnlyList<EntityId> candidateIds;
        if (options.EntityBoundsQueryInto is { } bufferedQuery)
        {
            candidateIdsBuffer.Clear();
            bufferedQuery(ownerBlockId, queryBounds, candidateIdsBuffer);
            candidateIds = candidateIdsBuffer;
        }
        else
        {
            candidateIds = options.EntityBoundsQuery!(ownerBlockId, queryBounds);
        }
        if (candidateIds.Count == 0)
            return [];

        candidatesBuffer.Clear();
        var candidateCapacity = Math.Min(candidateIds.Count, orderedEntities.Count);
        if (candidatesBuffer.Capacity < candidateCapacity)
            candidatesBuffer.Capacity = candidateCapacity;
        foreach (var entityId in candidateIds)
        {
            if (document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                entity.OwnerBlockId.Equals(ownerBlockId))
            {
                candidatesBuffer.Add(entity);
            }
        }

        if (candidatesBuffer.Count == 0)
            return [];

        if (orderedEntities.Count >= MinimumIndexedEntityCount &&
            candidatesBuffer.Count >= orderedEntities.Count / OrderedScanCandidateDivisor)
        {
            candidateSetBuffer.Clear();
            foreach (var candidate in candidatesBuffer)
                candidateSetBuffer.Add(candidate.Id);

            orderedCandidatesBuffer.Clear();
            if (orderedCandidatesBuffer.Capacity < candidatesBuffer.Count)
                orderedCandidatesBuffer.Capacity = candidatesBuffer.Count;
            foreach (var entity in orderedEntities)
            {
                if (candidateSetBuffer.Contains(entity.Id))
                    orderedCandidatesBuffer.Add(entity);
            }

            return orderedCandidatesBuffer;
        }

        entityOrderCache.SortCandidates(
            document,
            ownerBlockId,
            candidatesBuffer,
            rankedEntityBuffer);
        return candidatesBuffer;
    }

    private static bool TryResolveVisibleLocalBounds(
        Matrix3x2 localToScreen,
        CadViewport viewport,
        out CadRectD bounds)
    {
        bounds = CadRectD.Empty;
        if (viewport.ViewWidth <= 0.0 ||
            viewport.ViewHeight <= 0.0 ||
            !Matrix3x2.Invert(localToScreen, out var screenToLocal))
        {
            return false;
        }

        Span<Vector2> screenCorners =
        [
            Vector2.Zero,
            new Vector2((float)viewport.ViewWidth, 0.0f),
            new Vector2((float)viewport.ViewWidth, (float)viewport.ViewHeight),
            new Vector2(0.0f, (float)viewport.ViewHeight)
        ];
        foreach (var screenCorner in screenCorners)
        {
            var localPoint = Vector2.Transform(screenCorner, screenToLocal);
            if (!float.IsFinite(localPoint.X) || !float.IsFinite(localPoint.Y))
            {
                bounds = CadRectD.Empty;
                return false;
            }

            bounds = bounds.ExpandToInclude(new CadPointD(localPoint.X, localPoint.Y));
        }

        return !bounds.IsEmpty;
    }
}
