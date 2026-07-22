using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DBackgroundPreparationService
{
    private Task<PreparedDocumentPlan>? _pendingTask;
    private CadDocument? _pendingDocument;
    private long _pendingVersion;
    private PreparedDocumentPlan? _publishedPlan;

    public bool NeedsSchedule(CadDocument document, long version)
    {
        if (!ReferenceEquals(_pendingDocument, document) || _pendingVersion != version)
            return true;

        if (_publishedPlan is not null)
            return false;

        if (_pendingTask is null)
            return true;

        if (!_pendingTask.IsFaulted && !_pendingTask.IsCanceled)
            return false;

        _pendingTask = null;
        return true;
    }

    public void Schedule(
        CadDocument document,
        long version,
        IReadOnlyList<OwnerPreparationSnapshot> owners)
    {
        if (!NeedsSchedule(document, version))
            return;

        _pendingDocument = document;
        _pendingVersion = version;
        _publishedPlan = null;
        _pendingTask = Task.Run(() => Build(version, owners));
    }

    public PreparedDocumentPlan? TryGet(CadDocument document, long version)
    {
        if (!ReferenceEquals(_pendingDocument, document) || _pendingVersion != version)
            return null;
        if (_publishedPlan is not null)
            return _publishedPlan;
        if (_pendingTask is null || !_pendingTask.IsCompleted)
            return null;
        if (_pendingTask.IsCompletedSuccessfully && _pendingTask.Result.Version == version)
            _publishedPlan = _pendingTask.Result;
        _pendingTask = null;
        return _publishedPlan;
    }

    public void Invalidate()
    {
        _pendingTask = null;
        _pendingDocument = null;
        _publishedPlan = null;
    }

    private static PreparedDocumentPlan Build(
        long version,
        IReadOnlyList<OwnerPreparationSnapshot> owners)
    {
        var plans = new Dictionary<BlockId, PreparedOwnerPlan>(owners.Count);
        foreach (var owner in owners)
        {
            var ordered = owner.Entities
                .OrderBy(static entity => entity.LayerPriority)
                .ThenBy(static entity => entity.ZIndex)
                .ThenBy(static entity => entity.Entity.Id.Value)
                .ToArray();
            var bounds = CadRectD.Empty;
            var localWork = 0;
            foreach (var entity in ordered)
            {
                if (!entity.IsErased && entity.IsVisible)
                    bounds = bounds.Union(entity.Bounds);
                localWork = Math.Min(
                    Direct2DEntityOrderCache.MaximumEstimatedRenderWork,
                    localWork + entity.EstimatedRenderWork);
            }

            plans[owner.OwnerBlockId] = new PreparedOwnerPlan(
                ordered.Select(static entity => entity.Entity).ToArray(),
                bounds,
                localWork,
                BuildAdaptiveChunkBreaks(ordered),
                ordered.Select(static entity => entity.Entity.Id).ToArray(),
                ordered
                    .Where(static entity => entity.DefinitionBlockId is not null)
                    .Select(static entity => entity.DefinitionBlockId!.Value)
                    .ToArray());
        }

        foreach (var ownerId in plans.Keys.ToArray())
        {
            plans[ownerId].EstimatedRenderWork = ResolveRenderWork(plans, ownerId, []);
            plans[ownerId].DependencyEntityIds = ResolveDependencies(plans, ownerId, []);
        }

        return new PreparedDocumentPlan(version, plans);
    }

    private static IReadOnlySet<EntityId> BuildAdaptiveChunkBreaks(
        IReadOnlyList<EntityPreparationSnapshot> ordered)
    {
        const int minimumCount = 64;
        const int maximumCount = 384;
        var result = new HashSet<EntityId>();
        var count = 0;
        var bounds = CadRectD.Empty;
        var footprint = 0.0;
        foreach (var entity in ordered)
        {
            if (Direct2DAdaptiveChunkPlanner.ShouldFlushBefore(
                    count,
                    minimumCount,
                    maximumCount,
                    bounds,
                    footprint,
                    entity.Bounds))
            {
                result.Add(entity.Entity.Id);
                count = 0;
                bounds = CadRectD.Empty;
                footprint = 0;
            }

            count++;
            bounds = bounds.Union(entity.Bounds);
            footprint += Direct2DAdaptiveChunkPlanner.EstimateFootprint(entity.Bounds);
        }

        return result;
    }

    private static int ResolveRenderWork(
        IReadOnlyDictionary<BlockId, PreparedOwnerPlan> plans,
        BlockId ownerId,
        HashSet<BlockId> visiting)
    {
        if (!plans.TryGetValue(ownerId, out var owner) || !visiting.Add(ownerId))
            return 1;

        long total = owner.LocalEstimatedRenderWork;
        foreach (var nestedOwnerId in owner.NestedDefinitionBlockIds)
        {
            total += ResolveRenderWork(plans, nestedOwnerId, visiting);
            if (total >= Direct2DEntityOrderCache.MaximumEstimatedRenderWork)
            {
                total = Direct2DEntityOrderCache.MaximumEstimatedRenderWork;
                break;
            }
        }

        visiting.Remove(ownerId);
        return (int)total;
    }

    private static IReadOnlySet<EntityId> ResolveDependencies(
        IReadOnlyDictionary<BlockId, PreparedOwnerPlan> plans,
        BlockId ownerId,
        HashSet<BlockId> visiting)
    {
        if (!plans.TryGetValue(ownerId, out var owner) || !visiting.Add(ownerId))
            return new HashSet<EntityId>();

        var dependencies = new HashSet<EntityId>(owner.OwnedEntityIds);
        foreach (var nestedOwnerId in owner.NestedDefinitionBlockIds)
            dependencies.UnionWith(ResolveDependencies(plans, nestedOwnerId, visiting));
        visiting.Remove(ownerId);
        return dependencies;
    }
}

internal sealed record PreparedDocumentPlan(
    long Version,
    IReadOnlyDictionary<BlockId, PreparedOwnerPlan> Owners);

internal sealed class PreparedOwnerPlan(
    IReadOnlyList<CadEntity> orderedEntities,
    CadRectD bounds,
    int localEstimatedRenderWork,
    IReadOnlySet<EntityId> adaptiveChunkBreakEntityIds,
    IReadOnlyList<EntityId> ownedEntityIds,
    IReadOnlyList<BlockId> nestedDefinitionBlockIds)
{
    public IReadOnlyList<CadEntity> OrderedEntities { get; } = orderedEntities;
    public CadRectD Bounds { get; } = bounds;
    public int LocalEstimatedRenderWork { get; } = localEstimatedRenderWork;
    public IReadOnlySet<EntityId> AdaptiveChunkBreakEntityIds { get; } = adaptiveChunkBreakEntityIds;
    public IReadOnlyList<EntityId> OwnedEntityIds { get; } = ownedEntityIds;
    public IReadOnlyList<BlockId> NestedDefinitionBlockIds { get; } = nestedDefinitionBlockIds;
    public int EstimatedRenderWork { get; set; }
    public IReadOnlySet<EntityId> DependencyEntityIds { get; set; } = new HashSet<EntityId>();
}

internal sealed record OwnerPreparationSnapshot(
    BlockId OwnerBlockId,
    IReadOnlyList<EntityPreparationSnapshot> Entities);

internal readonly record struct EntityPreparationSnapshot(
    CadEntity Entity,
    int LayerPriority,
    int ZIndex,
    CadRectD Bounds,
    bool IsErased,
    bool IsVisible,
    int EstimatedRenderWork,
    BlockId? DefinitionBlockId);
