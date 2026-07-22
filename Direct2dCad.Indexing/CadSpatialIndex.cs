using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public sealed class CadSpatialIndex : ICadSpatialIndex
{
    private readonly Dictionary<EntityId, SpatialEntry> _entries = [];
    private readonly Dictionary<BlockId, OwnerIndex> _indexesByOwner = [];

    public int Count => _entries.Count;

    public void Add(EntityId entityId, CadRectD bounds) =>
        Add(entityId, BlockId.ModelSpace, bounds);

    public void Add(EntityId entityId, BlockId ownerBlockId, CadRectD bounds)
    {
        if (bounds.IsEmpty || !IsFinite(bounds))
        {
            Remove(entityId);
            return;
        }

        if (_entries.TryGetValue(entityId, out var previous))
        {
            if (previous.OwnerBlockId.Equals(ownerBlockId) && previous.Bounds.Equals(bounds))
                return;

            if (!previous.OwnerBlockId.Equals(ownerBlockId) &&
                _indexesByOwner.TryGetValue(previous.OwnerBlockId, out var previousIndex))
            {
                previousIndex.Remove(entityId);
                RemoveOwnerIfEmpty(previous.OwnerBlockId, previousIndex);
            }
        }

        _entries[entityId] = new SpatialEntry(ownerBlockId, bounds);
        GetOrCreateOwnerIndex(ownerBlockId).Add(entityId, bounds);
    }

    public void Remove(EntityId entityId)
    {
        if (!_entries.Remove(entityId, out var entry) ||
            !_indexesByOwner.TryGetValue(entry.OwnerBlockId, out var index))
        {
            return;
        }

        index.Remove(entityId);
        RemoveOwnerIfEmpty(entry.OwnerBlockId, index);
    }

    public void Update(EntityId entityId, CadRectD bounds)
    {
        var ownerBlockId = _entries.TryGetValue(entityId, out var entry)
            ? entry.OwnerBlockId
            : BlockId.ModelSpace;
        Update(entityId, ownerBlockId, bounds);
    }

    public void Update(EntityId entityId, BlockId ownerBlockId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            Remove(entityId);
        else
            Add(entityId, ownerBlockId, bounds);
    }

    public IReadOnlyList<EntityId> Query(CadRectD area)
    {
        if (area.IsEmpty || _entries.Count == 0)
            return [];

        var results = new List<EntityId>(Math.Min(_entries.Count, 256));
        foreach (var index in _indexesByOwner.Values)
            index.Query(area, results);
        return results;
    }

    public IReadOnlyList<EntityId> Query(BlockId ownerBlockId, CadRectD area)
    {
        if (area.IsEmpty || !_indexesByOwner.TryGetValue(ownerBlockId, out var index))
            return [];

        return index.Query(area);
    }

    public void Query(BlockId ownerBlockId, CadRectD area, List<EntityId> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (area.IsEmpty || !_indexesByOwner.TryGetValue(ownerBlockId, out var index))
            return;

        index.Query(area, results);
    }

    public void Clear()
    {
        _entries.Clear();
        _indexesByOwner.Clear();
    }

    public void Rebuild(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Clear();
        _entries.EnsureCapacity(document.Entities.Count);
        _indexesByOwner.EnsureCapacity(document.Blocks.Count);
        foreach (var entity in document.Entities.Values)
        {
            if (!entity.IsErased && entity.IsVisible)
            {
                var bounds = entity.Bounds;
                if (bounds.IsEmpty || !IsFinite(bounds))
                    continue;

                _entries.Add(entity.Id, new SpatialEntry(entity.OwnerBlockId, bounds));
                GetOrCreateOwnerIndex(document, entity.OwnerBlockId).Add(entity.Id, bounds);
            }
        }

        foreach (var index in _indexesByOwner.Values)
            index.RebuildTree();
    }

    private OwnerIndex GetOrCreateOwnerIndex(BlockId ownerBlockId)
    {
        if (_indexesByOwner.TryGetValue(ownerBlockId, out var index))
            return index;

        index = new OwnerIndex();
        _indexesByOwner.Add(ownerBlockId, index);
        return index;
    }

    private OwnerIndex GetOrCreateOwnerIndex(CadDocument document, BlockId ownerBlockId)
    {
        if (_indexesByOwner.TryGetValue(ownerBlockId, out var index))
            return index;

        var capacity = document.TryGetBlock(ownerBlockId, out var owner) && owner is not null
            ? owner.EntityIds.Count
            : 0;
        index = new OwnerIndex(capacity);
        _indexesByOwner.Add(ownerBlockId, index);
        return index;
    }

    private void RemoveOwnerIfEmpty(BlockId ownerBlockId, OwnerIndex index)
    {
        if (index.Count == 0)
            _indexesByOwner.Remove(ownerBlockId);
    }

    private static bool IsFinite(CadRectD bounds)
    {
        return double.IsFinite(bounds.MinX) &&
               double.IsFinite(bounds.MinY) &&
               double.IsFinite(bounds.MaxX) &&
               double.IsFinite(bounds.MaxY);
    }

    private readonly record struct SpatialEntry(BlockId OwnerBlockId, CadRectD Bounds);

    private sealed class OwnerIndex
    {
        private const int LeafCapacity = 8;
        private readonly Dictionary<EntityId, CadRectD> _boundsByEntity = [];
        private readonly HashSet<EntityId> _pendingChanges = [];
        private BvhNode? _root;

        public OwnerIndex(int capacity = 0)
        {
            if (capacity > 0)
                _boundsByEntity.EnsureCapacity(capacity);
        }

        public int Count => _boundsByEntity.Count;

        public void Add(EntityId entityId, CadRectD bounds)
        {
            if (_boundsByEntity.TryGetValue(entityId, out var previousBounds) &&
                previousBounds.Equals(bounds))
            {
                return;
            }

            _boundsByEntity[entityId] = bounds;
            MarkChanged(entityId);
        }

        public void Remove(EntityId entityId)
        {
            if (_boundsByEntity.Remove(entityId))
                MarkChanged(entityId);
        }

        public void Query(CadRectD area, List<EntityId> results)
        {
            EnsureTree();
            if (_root is null)
                return;

            var hasPendingChanges = _pendingChanges.Count > 0;
            QueryNode(_root, area, results, hasPendingChanges);
            if (!hasPendingChanges)
                return;

            foreach (var entityId in _pendingChanges)
            {
                if (_boundsByEntity.TryGetValue(entityId, out var bounds) &&
                    bounds.Intersects(area))
                {
                    results.Add(entityId);
                }
            }
        }

        public IReadOnlyList<EntityId> Query(CadRectD area)
        {
            EnsureTree();
            if (_root is null)
                return [];

            var capacity = area.Contains(_root.Bounds)
                ? _boundsByEntity.Count
                : Math.Min(_boundsByEntity.Count, 256);
            var results = new List<EntityId>(capacity);
            Query(area, results);
            return results;
        }

        private void MarkChanged(EntityId entityId)
        {
            if (_root is not null)
                _pendingChanges.Add(entityId);
        }

        private void EnsureTree()
        {
            if (_root is null || _pendingChanges.Count >= ResolveRebuildThreshold())
                RebuildTree();
        }

        private int ResolveRebuildThreshold() =>
            Math.Clamp(_boundsByEntity.Count / 32, 64, 512);

        public void RebuildTree()
        {
            if (_boundsByEntity.Count == 0)
            {
                _root = null;
                _pendingChanges.Clear();
                return;
            }

            var entries = new BvhEntry[_boundsByEntity.Count];
            var entryIndex = 0;
            foreach (var pair in _boundsByEntity)
                entries[entryIndex++] = new BvhEntry(pair.Key, pair.Value);

            OrderEntriesForBulkLoad(entries);
            _root = BuildNode(entries, 0, entries.Length);
            _pendingChanges.Clear();
        }

        private static void OrderEntriesForBulkLoad(BvhEntry[] entries)
        {
            Array.Sort(entries, BvhEntry.XComparer);
            var leafCount = (entries.Length + LeafCapacity - 1) / LeafCapacity;
            var sliceCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(leafCount)));
            var sliceLength = (entries.Length + sliceCount - 1) / sliceCount;
            sliceLength = ((sliceLength + LeafCapacity - 1) / LeafCapacity) * LeafCapacity;

            for (var start = 0; start < entries.Length; start += sliceLength)
            {
                Array.Sort(
                    entries,
                    start,
                    Math.Min(sliceLength, entries.Length - start),
                    BvhEntry.YComparer);
            }
        }

        private static BvhNode BuildNode(BvhEntry[] entries, int start, int length)
        {
            if (length <= LeafCapacity)
            {
                var leafBounds = CadRectD.Empty;
                for (var index = start; index < start + length; index++)
                    leafBounds = leafBounds.Union(entries[index].Bounds);
                return new BvhNode(leafBounds, entries, start, length);
            }

            var leftLength = length / 2;
            if (length > LeafCapacity * 2)
            {
                leftLength = Math.Max(
                    LeafCapacity,
                    leftLength / LeafCapacity * LeafCapacity);
            }

            var left = BuildNode(entries, start, leftLength);
            var right = BuildNode(entries, start + leftLength, length - leftLength);
            return new BvhNode(left.Bounds.Union(right.Bounds), left, right);
        }

        private void QueryNode(
            BvhNode node,
            CadRectD area,
            List<EntityId> results,
            bool excludePendingChanges)
        {
            if (!node.Bounds.Intersects(area))
                return;

            if (node.Entries is { } entries)
            {
                for (var index = node.EntryStart; index < node.EntryStart + node.EntryLength; index++)
                {
                    var entry = entries[index];
                    if ((!excludePendingChanges || !_pendingChanges.Contains(entry.EntityId)) &&
                        entry.Bounds.Intersects(area))
                    {
                        results.Add(entry.EntityId);
                    }
                }

                return;
            }

            if (node.Left is not null)
                QueryNode(node.Left, area, results, excludePendingChanges);
            if (node.Right is not null)
                QueryNode(node.Right, area, results, excludePendingChanges);
        }
    }

    private readonly record struct BvhEntry(EntityId EntityId, CadRectD Bounds)
    {
        public static IComparer<BvhEntry> XComparer { get; } =
            Comparer<BvhEntry>.Create(static (left, right) =>
                left.Bounds.Center.X.CompareTo(right.Bounds.Center.X));

        public static IComparer<BvhEntry> YComparer { get; } =
            Comparer<BvhEntry>.Create(static (left, right) =>
                left.Bounds.Center.Y.CompareTo(right.Bounds.Center.Y));
    }

    private sealed class BvhNode
    {
        public CadRectD Bounds { get; }
        public BvhNode? Left { get; }
        public BvhNode? Right { get; }
        public BvhEntry[]? Entries { get; }
        public int EntryStart { get; }
        public int EntryLength { get; }

        public BvhNode(
            CadRectD bounds,
            BvhEntry[] entries,
            int entryStart,
            int entryLength)
        {
            Bounds = bounds;
            Entries = entries;
            EntryStart = entryStart;
            EntryLength = entryLength;
        }

        public BvhNode(CadRectD bounds, BvhNode left, BvhNode right)
        {
            Bounds = bounds;
            Left = left;
            Right = right;
        }
    }
}
