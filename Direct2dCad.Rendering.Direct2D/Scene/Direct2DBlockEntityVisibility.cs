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
        Direct2DEntityOrderCache entityOrderCache)
    {
        var orderedEntities = entityOrderCache.GetOrderedEntities(document, ownerBlockId);
        if (orderedEntities.Count < MinimumIndexedEntityCount ||
            options.EntityBoundsQuery is not { } query ||
            !TryResolveVisibleLocalBounds(localToScreen, viewport, out var visibleBounds))
        {
            return orderedEntities;
        }

        var screenScale = Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(localToScreen);
        var queryBounds = visibleBounds.Inflate(PaintPaddingPixels / screenScale);
        var ownerBounds = entityOrderCache.GetOwnerBounds(document, ownerBlockId);
        if (!ownerBounds.IsEmpty && queryBounds.Contains(ownerBounds))
            return orderedEntities;

        var candidateIds = query(ownerBlockId, queryBounds);
        if (candidateIds.Count == 0)
            return [];

        var candidates = new List<CadEntity>(Math.Min(candidateIds.Count, orderedEntities.Count));
        foreach (var entityId in candidateIds)
        {
            if (document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                entity.OwnerBlockId.Equals(ownerBlockId))
            {
                candidates.Add(entity);
            }
        }

        if (candidates.Count == 0)
            return [];

        if (orderedEntities.Count >= MinimumIndexedEntityCount &&
            candidates.Count >= orderedEntities.Count / OrderedScanCandidateDivisor)
        {
            var candidateSet = new HashSet<EntityId>(candidates.Count);
            foreach (var candidate in candidates)
                candidateSet.Add(candidate.Id);

            var orderedCandidates = new List<CadEntity>(candidates.Count);
            foreach (var entity in orderedEntities)
            {
                if (candidateSet.Contains(entity.Id))
                    orderedCandidates.Add(entity);
            }

            return orderedCandidates;
        }

        candidates.Sort(entityOrderCache.GetComparer(document, ownerBlockId));
        return candidates;
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
