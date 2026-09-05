using System.Collections.Frozen;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DBackgroundPreparationService : IDisposable
{
    private Task<PreparedDocumentPlan>? _pendingTask;
    private CancellationTokenSource? _pendingCancellation;
    private CadDocument? _pendingDocument;
    private long _pendingVersion;
    private PreparedDocumentPlan? _publishedPlan;
    private CadDocument? _reusableDocument;
    private IReadOnlyDictionary<BlockId, PreparedOwnerPlan> _reusableOwners = FrozenDictionary<BlockId, PreparedOwnerPlan>.Empty;

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

        ClearPendingTask(cancel: false);
        return true;
    }

    public void Schedule(
        CadDocument document,
        long version,
        IReadOnlyList<OwnerPreparationSnapshot> owners)
    {
        if (!NeedsSchedule(document, version))
            return;

        // A document/version change supersedes the previous preparation. Do not
        // leave the old worker running until it happens to observe cancellation.
        if (_pendingTask is not null || _pendingCancellation is not null)
            ClearPendingTask(cancel: true);

        _pendingDocument = document;
        _pendingVersion = version;
        _publishedPlan = null;
        _pendingCancellation = new CancellationTokenSource();
        var cancellationToken = _pendingCancellation.Token;
        var reusableOwners = ReferenceEquals(_reusableDocument, document)
            ? _reusableOwners
            : null;
        _pendingTask = Task.Run(
            () => Build(version, owners, reusableOwners, cancellationToken),
            cancellationToken);
    }

    public PreparedDocumentPlan? TryGet(CadDocument document, long version)
    {
        if (!ReferenceEquals(_pendingDocument, document) || _pendingVersion != version)
            return null;
        if (_publishedPlan is not null)
            return _publishedPlan;
        if (_pendingTask is null || !_pendingTask.IsCompleted)
            return null;
        if (_pendingTask.IsCompletedSuccessfully)
        {
            var plan = _pendingTask.GetAwaiter().GetResult();
            if (plan.Version == version)
            {
                _publishedPlan = plan;
                _reusableDocument = document;
                _reusableOwners = plan.Owners;
            }
        }
        ClearPendingTask(cancel: false);
        return _publishedPlan;
    }

    public void Invalidate(bool preserveReusableOwners = false)
    {
        if (preserveReusableOwners && _pendingDocument is { } document)
            TryGet(document, _pendingVersion);
        ClearPendingTask(cancel: true);
        _pendingDocument = null;
        _pendingVersion = 0;
        _publishedPlan = null;
        if (!preserveReusableOwners)
        {
            _reusableDocument = null;
            _reusableOwners = FrozenDictionary<BlockId, PreparedOwnerPlan>.Empty;
        }
    }

    public void Dispose() => Invalidate();

    private static PreparedDocumentPlan Build(
        long version,
        IReadOnlyList<OwnerPreparationSnapshot> owners,
        IReadOnlyDictionary<BlockId, PreparedOwnerPlan>? reusableOwners,
        CancellationToken cancellationToken)
    {
        var plans = new Dictionary<BlockId, PreparedOwnerPlan>(owners.Count);
        foreach (var owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reusableOwners is not null &&
                reusableOwners.TryGetValue(owner.OwnerBlockId, out var previous) &&
                ReferenceEquals(previous.SourceSnapshot, owner))
            {
                // Local arrays are immutable. Dependency metrics belong to this new plan.
                plans[owner.OwnerBlockId] = new PreparedOwnerPlan(
                    previous.OrderedEntities, previous.Bounds, previous.LocalEstimatedRenderWork,
                    previous.AdaptiveChunkBreakEntityIds, previous.OwnedEntityIds,
                    previous.NestedDefinitionBlockIds) { SourceSnapshot = owner };
                continue;
            }
            var ordered = owner.Entities
                .OrderBy(static entity => entity.LayerPriority)
                .ThenBy(static entity => entity.ZIndex)
                .ThenBy(static entity => entity.InsertionIndex)
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
                BuildAdaptiveChunkBreaks(ordered, cancellationToken),
                ordered.Select(static entity => entity.Entity.Id).ToArray(),
                ordered
                    .Where(static entity => entity.DefinitionBlockId is not null)
                    .Select(static entity => entity.DefinitionBlockId!.Value)
                    .ToArray()) { SourceSnapshot = owner };
        }

        var workCache = new Dictionary<BlockId, int>(plans.Count);
        var visiting = new HashSet<BlockId>();
        foreach (var ownerId in plans.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plans[ownerId].EstimatedRenderWork = ResolveRenderWork(
                plans,
                ownerId,
                visiting,
                workCache,
                cancellationToken,
                out _);
            plans[ownerId].DependencyEntityIds = ResolveDependencies(
                plans,
                ownerId,
                cancellationToken);
        }

        return new PreparedDocumentPlan(version, plans);
    }

    private static IReadOnlySet<EntityId> BuildAdaptiveChunkBreaks(
        IReadOnlyList<EntityPreparationSnapshot> ordered,
        CancellationToken cancellationToken)
    {
        const int minimumCount = 64;
        const int maximumCount = 384;
        var result = new HashSet<EntityId>();
        var count = 0;
        var bounds = CadRectD.Empty;
        var footprint = 0.0;
        foreach (var entity in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        HashSet<BlockId> visiting,
        Dictionary<BlockId, int> cache,
        CancellationToken cancellationToken,
        out bool containsCycle)
    {
        cancellationToken.ThrowIfCancellationRequested();
        containsCycle = false;
        if (cache.TryGetValue(ownerId, out var cached))
            return cached;
        if (!plans.TryGetValue(ownerId, out var owner))
            return 1;
        if (!visiting.Add(ownerId))
        {
            containsCycle = true;
            return 1;
        }

        long total = owner.LocalEstimatedRenderWork;
        foreach (var nestedOwnerId in owner.NestedDefinitionBlockIds)
        {
            total += ResolveRenderWork(plans, nestedOwnerId, visiting, cache,
                cancellationToken, out var nestedCycle);
            containsCycle |= nestedCycle;
            if (total >= Direct2DEntityOrderCache.MaximumEstimatedRenderWork)
            {
                total = Direct2DEntityOrderCache.MaximumEstimatedRenderWork;
                break;
            }
        }

        visiting.Remove(ownerId);
        // Cyclic costs depend on the current recursion path and cannot be memoized.
        if (!containsCycle)
            cache[ownerId] = (int)total;
        return (int)total;
    }

    private static IReadOnlySet<EntityId> ResolveDependencies(
        IReadOnlyDictionary<BlockId, PreparedOwnerPlan> plans,
        BlockId ownerId,
        CancellationToken cancellationToken)
    {
        var dependencies = new HashSet<EntityId>();
        var visited = new HashSet<BlockId> { ownerId };
        var pending = new Stack<BlockId>();
        pending.Push(ownerId);
        // Dependencies are a set: repeated block instances only need one visit.
        while (pending.TryPop(out var currentId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!plans.TryGetValue(currentId, out var owner))
                continue;

            dependencies.UnionWith(owner.OwnedEntityIds);
            foreach (var nestedOwnerId in owner.NestedDefinitionBlockIds)
            {
                if (visited.Add(nestedOwnerId))
                    pending.Push(nestedOwnerId);
            }
        }
        return dependencies;
    }

    private void ClearPendingTask(bool cancel)
    {
        if (cancel)
            _pendingCancellation?.Cancel();
        _pendingCancellation?.Dispose();
        _pendingCancellation = null;
        _pendingTask = null;
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
    public OwnerPreparationSnapshot? SourceSnapshot { get; init; }
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
    int InsertionIndex,
    CadRectD Bounds,
    bool IsErased,
    bool IsVisible,
    int EstimatedRenderWork,
    BlockId? DefinitionBlockId);
