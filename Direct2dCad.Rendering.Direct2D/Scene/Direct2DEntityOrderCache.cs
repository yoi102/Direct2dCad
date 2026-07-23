using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DEntityOrderCache : IDisposable
{
    internal const int MaximumEstimatedRenderWork = 1_000_000;
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _entitiesByOwner = [];
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _oleEntitiesByOwner = [];
    private readonly Dictionary<BlockId, IReadOnlyDictionary<EntityId, int>> _ranksByOwner = [];
    private readonly Dictionary<BlockId, CadRectD> _boundsByOwner = [];
    private readonly Dictionary<BlockId, int> _estimatedRenderWorkByOwner = [];
    private readonly Direct2DBackgroundPreparationService _backgroundPreparation = new();
    private CadDocument? _document;
    private long _preparationVersion;

    public IReadOnlyList<CadEntity> GetOrderedEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureDocument(document);

        if (_entitiesByOwner.TryGetValue(ownerBlockId, out var entities))
            return entities;

        if (TryGetPreparedOwner(document, ownerBlockId, out var prepared))
        {
            entities = prepared.OrderedEntities;
        }
        else
        {
            var ownerEntities = ResolveOwnerEntities(document, ownerBlockId);
            entities = ownerEntities
                .OrderBy(entity =>
                    document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
                .ThenBy(entity => entity.ZIndex)
                .ThenBy(entity => entity.Id.Value)
                .ToArray();
        }
        _entitiesByOwner[ownerBlockId] = entities;
        return entities;
    }

    private static IEnumerable<CadEntity> ResolveOwnerEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        if (!document.TryGetBlock(ownerBlockId, out var owner) || owner is null)
            return [];

        var entities = new List<CadEntity>(owner.EntityIds.Count);
        foreach (var entityId in owner.EntityIds)
        {
            if (document.TryGetEntity(entityId, out var entity) && entity is not null)
                entities.Add(entity);
        }

        return entities;
    }

    public void SortCandidates(
        CadDocument document,
        BlockId ownerBlockId,
        List<CadEntity> candidates,
        List<RankedEntity> rankedBuffer)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rankedBuffer);
        if (candidates.Count < 2)
            return;

        var ranks = GetEntityRanks(document, ownerBlockId);
        rankedBuffer.Clear();
        if (rankedBuffer.Capacity < candidates.Count)
            rankedBuffer.Capacity = candidates.Count;
        foreach (var entity in candidates)
        {
            rankedBuffer.Add(new RankedEntity(
                ranks.GetValueOrDefault(entity.Id, int.MaxValue),
                entity));
        }

        rankedBuffer.Sort(RankedEntityComparer.Instance);
        candidates.Clear();
        foreach (var ranked in rankedBuffer)
            candidates.Add(ranked.Entity);
    }

    private IReadOnlyDictionary<EntityId, int> GetEntityRanks(
        CadDocument document,
        BlockId ownerBlockId)
    {
        if (_ranksByOwner.TryGetValue(ownerBlockId, out var ranks) &&
            ReferenceEquals(_document, document))
        {
            return ranks;
        }

        var entities = GetOrderedEntities(document, ownerBlockId);
        var created = new Dictionary<EntityId, int>(entities.Count);
        for (var index = 0; index < entities.Count; index++)
            created[entities[index].Id] = index;
        _ranksByOwner[ownerBlockId] = created;
        return created;
    }

    public IReadOnlyList<CadEntity> GetOrderedOleEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        if (_oleEntitiesByOwner.TryGetValue(ownerBlockId, out var oleEntities) &&
            ReferenceEquals(_document, document))
        {
            return oleEntities;
        }

        oleEntities = GetOrderedEntities(document, ownerBlockId)
            .Where(static entity => entity is CadOleObject)
            .ToArray();
        _oleEntitiesByOwner[ownerBlockId] = oleEntities;
        return oleEntities;
    }

    public int GetEstimatedRenderWork(CadDocument document, BlockId ownerBlockId)
    {
        ArgumentNullException.ThrowIfNull(document);
        GetOrderedEntities(document, ownerBlockId);
        if (_estimatedRenderWorkByOwner.TryGetValue(ownerBlockId, out var cached))
            return cached;
        if (TryGetPreparedOwner(document, ownerBlockId, out var prepared))
        {
            _estimatedRenderWorkByOwner[ownerBlockId] = prepared.EstimatedRenderWork;
            return prepared.EstimatedRenderWork;
        }
        return EstimateOwnerRenderWork(document, ownerBlockId, []);
    }

    public IReadOnlySet<EntityId>? GetAdaptiveChunkBreakEntityIds(
        CadDocument document,
        BlockId ownerBlockId) =>
        TryGetPreparedOwner(document, ownerBlockId, out var prepared)
            ? prepared.AdaptiveChunkBreakEntityIds
            : null;

    public IReadOnlySet<EntityId>? GetPreparedDependencyEntityIds(
        CadDocument document,
        BlockId ownerBlockId) =>
        TryGetPreparedOwner(document, ownerBlockId, out var prepared)
            ? prepared.DependencyEntityIds
            : null;

    public void ScheduleBackgroundPreparation(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureDocument(document);
        if (!_backgroundPreparation.NeedsSchedule(document, _preparationVersion))
            return;

        var owners = new List<OwnerPreparationSnapshot>(document.Blocks.Count);
        foreach (var block in document.Blocks.Values)
        {
            var entities = new List<EntityPreparationSnapshot>(block.EntityIds.Count);
            foreach (var entityId in block.EntityIds)
            {
                if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                    continue;
                entities.Add(new EntityPreparationSnapshot(
                    entity,
                    document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId),
                    entity.ZIndex,
                    entity.Bounds,
                    entity.IsErased,
                    entity.IsVisible,
                    EstimateEntityRenderWork(document, entity),
                    entity is CadBlockReference reference
                        ? reference.DefinitionBlockId
                        : null));
            }

            owners.Add(new OwnerPreparationSnapshot(block.Id, entities));
        }

        _backgroundPreparation.Schedule(document, _preparationVersion, owners);
    }

    public void InvalidateOwnerMetrics()
    {
        _boundsByOwner.Clear();
        _estimatedRenderWorkByOwner.Clear();
        InvalidatePreparedPlan();
    }

    public void Invalidate()
    {
        _entitiesByOwner.Clear();
        _oleEntitiesByOwner.Clear();
        _ranksByOwner.Clear();
        _boundsByOwner.Clear();
        _estimatedRenderWorkByOwner.Clear();
        InvalidatePreparedPlan();
    }

    public CadRectD GetOwnerBounds(CadDocument document, BlockId ownerBlockId)
    {
        if (_boundsByOwner.TryGetValue(ownerBlockId, out var bounds) &&
            ReferenceEquals(_document, document))
        {
            return bounds;
        }

        if (TryGetPreparedOwner(document, ownerBlockId, out var prepared))
        {
            _boundsByOwner[ownerBlockId] = prepared.Bounds;
            return prepared.Bounds;
        }

        bounds = CadRectD.Empty;
        foreach (var entity in GetOrderedEntities(document, ownerBlockId))
        {
            if (!entity.IsErased && entity.IsVisible)
                bounds = bounds.Union(entity.Bounds);
        }

        _boundsByOwner[ownerBlockId] = bounds;
        return bounds;
    }

    private int EstimateOwnerRenderWork(
        CadDocument document,
        BlockId ownerBlockId,
        HashSet<BlockId> visitingBlocks)
    {
        if (_estimatedRenderWorkByOwner.TryGetValue(ownerBlockId, out var cached))
            return cached;
        if (!visitingBlocks.Add(ownerBlockId))
            return 1;

        long total = 0;
        try
        {
            foreach (var entity in GetOrderedEntities(document, ownerBlockId))
            {
                if (entity.IsErased || !entity.IsVisible)
                    continue;

                total += EstimateEntityRenderWork(document, entity);
                if (entity is CadBlockReference reference)
                {
                    total += EstimateOwnerRenderWork(
                        document,
                        reference.DefinitionBlockId,
                        visitingBlocks);
                }

                if (total >= MaximumEstimatedRenderWork)
                {
                    total = MaximumEstimatedRenderWork;
                    break;
                }
            }
        }
        finally
        {
            visitingBlocks.Remove(ownerBlockId);
        }

        var estimate = (int)total;
        _estimatedRenderWorkByOwner[ownerBlockId] = estimate;
        return estimate;
    }

    private static int EstimateEntityRenderWork(CadDocument document, CadEntity entity)
    {
        var work = entity switch
        {
            CadPolyline polyline => Math.Max(1, polyline.Points.Count / 8),
            CadSpline spline => Math.Max(1, spline.FitPoints.Count / 2),
            CadShapeText shapeText => Math.Max(1, shapeText.Text.Length / 4),
            CadText => 2,
            _ => 1
        };

        if (!TryResolveHatchStyle(document, entity, out var hatchStyle) ||
            !document.TryGetHatchPattern(hatchStyle.PatternId, out var pattern) ||
            pattern is null)
        {
            return work;
        }

        var extent = Math.Max(entity.Bounds.Width, entity.Bounds.Height);
        var hatchScale = hatchStyle.HatchScale;
        if (!double.IsFinite(extent) || extent <= 0.0 ||
            !double.IsFinite(hatchScale) || hatchScale <= double.Epsilon)
        {
            return work;
        }

        double hatchWork = 0.0;
        foreach (var line in pattern.Lines)
        {
            var spacing = Math.Max(line.Offset.Length * hatchScale, hatchScale * 0.01);
            var lineCount = Math.Max(1.0, extent / spacing);
            var dashFactor = line.IsSolidLine
                ? 1
                : Math.Clamp(line.DashPattern.Count, 1, 8);
            hatchWork += lineCount * dashFactor;
        }

        return Math.Max(
            work,
            (int)Math.Clamp(Math.Ceiling(hatchWork), 1.0, MaximumEstimatedRenderWork));
    }

    private static bool TryResolveHatchStyle(
        CadDocument document,
        CadEntity entity,
        out CadHatchFillStyle hatchStyle)
    {
        var fillStyleId = entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            CadSpline { Closed: true } spline => spline.FillStyleId,
            _ => null
        };

        if (fillStyleId is { } styleId &&
            document.TryGetStyle(styleId, out var style) &&
            style is CadHatchFillStyle resolved)
        {
            hatchStyle = resolved;
            return true;
        }

        hatchStyle = null!;
        return false;
    }

    private void EnsureDocument(CadDocument document)
    {
        if (ReferenceEquals(_document, document))
            return;

        _document = document;
        _entitiesByOwner.Clear();
        _oleEntitiesByOwner.Clear();
        _ranksByOwner.Clear();
        _boundsByOwner.Clear();
        _estimatedRenderWorkByOwner.Clear();
        InvalidatePreparedPlan();
    }

    private bool TryGetPreparedOwner(
        CadDocument document,
        BlockId ownerBlockId,
        out PreparedOwnerPlan prepared)
    {
        var plan = _backgroundPreparation.TryGet(document, _preparationVersion);
        if (plan is not null && plan.Owners.TryGetValue(ownerBlockId, out prepared!))
            return true;
        prepared = null!;
        return false;
    }

    private void InvalidatePreparedPlan()
    {
        _preparationVersion++;
        _backgroundPreparation.Invalidate();
    }

    public void Dispose() => _backgroundPreparation.Dispose();

    internal readonly record struct RankedEntity(int Rank, CadEntity Entity);

    private sealed class RankedEntityComparer : IComparer<RankedEntity>
    {
        public static RankedEntityComparer Instance { get; } = new();

        public int Compare(RankedEntity left, RankedEntity right)
        {
            var result = left.Rank.CompareTo(right.Rank);
            return result != 0
                ? result
                : left.Entity.Id.Value.CompareTo(right.Entity.Id.Value);
        }
    }
}
