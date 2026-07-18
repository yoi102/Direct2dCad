using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public sealed class CadSpatialIndex : ICadSpatialIndex
{
    private const int LeafCapacity = 8;
    private readonly Dictionary<EntityId, CadRectD> _boundsByEntity = [];
    private readonly HashSet<EntityId> _pendingChanges = [];
    private BvhNode? _root;

    public int Count => _boundsByEntity.Count;

    public void Add(EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty || !IsFinite(bounds))
        {
            Remove(entityId);
            return;
        }

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

    public void Update(EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            Remove(entityId);
        else
            Add(entityId, bounds);
    }

    public IReadOnlyList<EntityId> Query(CadRectD area)
    {
        if (area.IsEmpty)
            return [];

        EnsureTree();
        if (_root is null)
            return [];

        var results = new List<EntityId>(
            area.Contains(_root.Bounds)
                ? _boundsByEntity.Count
                : Math.Min(_boundsByEntity.Count, 256));
        var hasPendingChanges = _pendingChanges.Count > 0;
        QueryNode(_root, area, results, hasPendingChanges);

        if (!hasPendingChanges)
            return results;

        foreach (var entityId in _pendingChanges)
        {
            if (_boundsByEntity.TryGetValue(entityId, out var bounds) &&
                bounds.Intersects(area))
            {
                results.Add(entityId);
            }
        }

        return results;
    }

    public void Clear()
    {
        _boundsByEntity.Clear();
        _pendingChanges.Clear();
        _root = null;
    }

    public void Rebuild(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Clear();
        foreach (var entity in document.Entities.Values)
        {
            if (!entity.IsErased && entity.IsVisible)
                Add(entity.Id, entity.Bounds);
        }

        RebuildTree();
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

    private int ResolveRebuildThreshold()
    {
        return Math.Clamp(_boundsByEntity.Count / 32, 64, 512);
    }

    private void RebuildTree()
    {
        if (_boundsByEntity.Count == 0)
        {
            _root = null;
            _pendingChanges.Clear();
            return;
        }

        var entries = new BvhEntry[_boundsByEntity.Count];
        var index = 0;
        foreach (var pair in _boundsByEntity)
            entries[index++] = new BvhEntry(pair.Key, pair.Value);

        _root = BuildNode(entries, 0, entries.Length);
        _pendingChanges.Clear();
    }

    private BvhNode BuildNode(BvhEntry[] entries, int start, int length)
    {
        var bounds = CadRectD.Empty;
        for (var index = start; index < start + length; index++)
            bounds = bounds.Union(entries[index].Bounds);

        if (length <= LeafCapacity)
        {
            var leafEntries = new BvhEntry[length];
            Array.Copy(entries, start, leafEntries, 0, length);
            return new BvhNode(bounds, leafEntries);
        }

        var sortByX = bounds.Width >= bounds.Height;
        Array.Sort(
            entries,
            start,
            length,
            sortByX ? BvhEntry.XComparer : BvhEntry.YComparer);

        var leftLength = length / 2;
        var left = BuildNode(entries, start, leftLength);
        var right = BuildNode(entries, start + leftLength, length - leftLength);
        return new BvhNode(bounds, left, right);
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
            foreach (var entry in entries)
            {
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

    private static bool IsFinite(CadRectD bounds)
    {
        return double.IsFinite(bounds.MinX) &&
               double.IsFinite(bounds.MinY) &&
               double.IsFinite(bounds.MaxX) &&
               double.IsFinite(bounds.MaxY);
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

        public BvhNode(CadRectD bounds, BvhEntry[] entries)
        {
            Bounds = bounds;
            Entries = entries;
        }

        public BvhNode(CadRectD bounds, BvhNode left, BvhNode right)
        {
            Bounds = bounds;
            Left = left;
            Right = right;
        }
    }
}
