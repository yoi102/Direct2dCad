using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DEntityOrderCache
{
    private const int MaximumEstimatedRenderWork = 1_000_000;
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _entitiesByOwner = [];
    private readonly Dictionary<BlockId, IReadOnlyList<CadEntity>> _oleEntitiesByOwner = [];
    private readonly Dictionary<BlockId, IComparer<CadEntity>> _comparersByOwner = [];
    private readonly Dictionary<BlockId, int> _estimatedRenderWorkByOwner = [];
    private CadDocument? _document;

    public IReadOnlyList<CadEntity> GetOrderedEntities(
        CadDocument document,
        BlockId ownerBlockId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!ReferenceEquals(_document, document))
        {
            _document = document;
            _entitiesByOwner.Clear();
            _oleEntitiesByOwner.Clear();
            _comparersByOwner.Clear();
            _estimatedRenderWorkByOwner.Clear();
        }

        if (_entitiesByOwner.TryGetValue(ownerBlockId, out var entities))
            return entities;

        entities = document.Entities.Values
            .Where(entity => entity.OwnerBlockId.Equals(ownerBlockId))
            .OrderBy(entity =>
                document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenBy(entity => entity.ZIndex)
            .ThenBy(entity => entity.Id.Value)
            .ToArray();
        _entitiesByOwner[ownerBlockId] = entities;
        return entities;
    }

    public IComparer<CadEntity> GetComparer(
        CadDocument document,
        BlockId ownerBlockId)
    {
        if (_comparersByOwner.TryGetValue(ownerBlockId, out var comparer) &&
            ReferenceEquals(_document, document))
        {
            return comparer;
        }

        var entities = GetOrderedEntities(document, ownerBlockId);
        var ranks = new Dictionary<EntityId, int>(entities.Count);
        for (var index = 0; index < entities.Count; index++)
            ranks[entities[index].Id] = index;

        comparer = new EntityRankComparer(ranks);
        _comparersByOwner[ownerBlockId] = comparer;
        return comparer;
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
        return EstimateOwnerRenderWork(document, ownerBlockId, []);
    }

    public void InvalidateRenderWorkEstimates() => _estimatedRenderWorkByOwner.Clear();

    public void Invalidate()
    {
        _entitiesByOwner.Clear();
        _oleEntitiesByOwner.Clear();
        _comparersByOwner.Clear();
        _estimatedRenderWorkByOwner.Clear();
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

                total++;
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

    private sealed class EntityRankComparer(
        IReadOnlyDictionary<EntityId, int> ranks) : IComparer<CadEntity>
    {
        public int Compare(CadEntity? left, CadEntity? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            var leftRank = ranks.GetValueOrDefault(left.Id, int.MaxValue);
            var rightRank = ranks.GetValueOrDefault(right.Id, int.MaxValue);
            var result = leftRank.CompareTo(rightRank);
            return result != 0
                ? result
                : left.Id.Value.CompareTo(right.Id.Value);
        }
    }
}
