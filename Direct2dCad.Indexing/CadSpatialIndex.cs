using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public sealed class CadSpatialIndex : ICadSpatialIndex
{
    private static readonly SemaphoreSlim BackgroundBuildSlots =
        new(Math.Clamp(Environment.ProcessorCount / 2, 1, 2));
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
        foreach (var index in _indexesByOwner.Values)
            index.CancelBuild();
        _entries.Clear();
        _indexesByOwner.Clear();
    }

    public int CountIntersecting(BlockId ownerBlockId, CadRectD area) =>
        !area.IsEmpty && _indexesByOwner.TryGetValue(ownerBlockId, out var index)
            ? index.CountIntersecting(area)
            : 0;

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
        {
            index.CancelBuild();
            _indexesByOwner.Remove(ownerBlockId);
        }
    }

    internal Task PendingRebuilds => Task.WhenAll(
        _indexesByOwner.Values.Select(index => index.PendingBuild ?? Task.CompletedTask));

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
        private const int MinimumBackgroundBuildCount = 1024;
        private readonly Dictionary<EntityId, CadRectD> _boundsByEntity = [];
        private Dictionary<EntityId, CadRectD> _pendingChanges = [];
        private Dictionary<EntityId, CadRectD>? _changesDuringBuild;
        private Task<BvhNode>? _pendingBuild;
        private CancellationTokenSource? _buildCancellation;
        private BvhNode? _root;

        public Task? PendingBuild => _pendingBuild;

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

            MarkChanged(entityId, _boundsByEntity.GetValueOrDefault(entityId, CadRectD.Empty));
            _boundsByEntity[entityId] = bounds;
        }

        public void Remove(EntityId entityId)
        {
            if (_boundsByEntity.Remove(entityId, out var previous))
                MarkChanged(entityId, previous);
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

            foreach (var entityId in _pendingChanges.Keys)
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

        private void MarkChanged(EntityId entityId, CadRectD previous)
        {
            if (_root is not null)
                _pendingChanges.TryAdd(entityId, previous);
            _changesDuringBuild?.TryAdd(entityId, previous);
        }

        public int CountIntersecting(CadRectD area)
        {
            EnsureTree();
            if (_root is null)
                return 0;
            var count = CountNode(_root, area);
            foreach (var (id, original) in _pendingChanges)
            {
                if (!original.IsEmpty && original.Intersects(area))
                    count--;
                if (_boundsByEntity.TryGetValue(id, out var bounds) && bounds.Intersects(area))
                    count++;
            }
            return count;
        }

        private static int CountNode(BvhNode node, CadRectD area)
        {
            if (!node.Bounds.Intersects(area))
                return 0;
            var containsNode = area.Contains(node.Bounds);
            if (containsNode)
                return node.EntryLength;
            if (node.Left is null)
            {
                var count = 0;
                for (var index = node.EntryStart; index < node.EntryStart + node.EntryLength; index++)
                {
                    var entry = node.Entries[index];
                    if (entry.Bounds.Intersects(area))
                        count++;
                }
                return count;
            }
            return CountNode(node.Left, area) +
                   (node.Right is null ? 0 : CountNode(node.Right, area));
        }

        private void EnsureTree()
        {
            PublishCompletedBuild();
            if (_root is null)
                RebuildTree();
            else if (_pendingBuild is null && _pendingChanges.Count >= ResolveRebuildThreshold())
            {
                if (_boundsByEntity.Count < MinimumBackgroundBuildCount)
                    RebuildTree();
                else
                    ScheduleBuild();
            }
        }

        private void ScheduleBuild()
        {
            var entries = CaptureEntries();
            _changesDuringBuild = [];
            _buildCancellation = new CancellationTokenSource();
            var token = _buildCancellation.Token;
            // Only value snapshots cross threads; the live document/index remains thread-affine.
            _pendingBuild = Task.Run(async () =>
            {
                await BackgroundBuildSlots.WaitAsync(token).ConfigureAwait(false);
                try { return BuildSnapshot(entries, token); }
                finally { BackgroundBuildSlots.Release(); }
            }, token);
            _ = _pendingBuild.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void PublishCompletedBuild()
        {
            if (_pendingBuild is not { IsCompleted: true } task)
                return;
            if (task.IsCompletedSuccessfully)
            {
                _root = task.Result;
                _pendingChanges = _changesDuringBuild!;
            }
            _pendingBuild = null;
            _changesDuringBuild = null;
            _buildCancellation?.Dispose();
            _buildCancellation = null;
        }

        public void CancelBuild()
        {
            _buildCancellation?.Cancel();
            _buildCancellation?.Dispose();
            _buildCancellation = null;
            _pendingBuild = null;
            _changesDuringBuild = null;
        }

        private int ResolveRebuildThreshold() =>
            Math.Clamp(_boundsByEntity.Count / 32, 64, 512);

        public void RebuildTree()
        {
            CancelBuild();
            if (_boundsByEntity.Count == 0)
            {
                _root = null;
                _pendingChanges.Clear();
                return;
            }

            _root = BuildSnapshot(CaptureEntries(), CancellationToken.None);
            _pendingChanges.Clear();
        }

        private BvhEntry[] CaptureEntries()
        {
            var entries = new BvhEntry[_boundsByEntity.Count];
            var entryIndex = 0;
            foreach (var pair in _boundsByEntity)
                entries[entryIndex++] = new BvhEntry(pair.Key, pair.Value);

            return entries;
        }

        private static BvhNode BuildSnapshot(BvhEntry[] entries, CancellationToken token)
        {
            OrderEntriesForBulkLoad(entries, token);
            return BuildNode(entries, 0, entries.Length, token);
        }

        private static void OrderEntriesForBulkLoad(BvhEntry[] entries, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Array.Sort(entries, BvhEntry.XComparer);
            var leafCount = (entries.Length + LeafCapacity - 1) / LeafCapacity;
            var sliceCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(leafCount)));
            var sliceLength = (entries.Length + sliceCount - 1) / sliceCount;
            sliceLength = ((sliceLength + LeafCapacity - 1) / LeafCapacity) * LeafCapacity;

            for (var start = 0; start < entries.Length; start += sliceLength)
            {
                token.ThrowIfCancellationRequested();
                Array.Sort(
                    entries,
                    start,
                    Math.Min(sliceLength, entries.Length - start),
                    BvhEntry.YComparer);
            }
        }

        private static BvhNode BuildNode(BvhEntry[] entries, int start, int length, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
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

            var left = BuildNode(entries, start, leftLength, token);
            var right = BuildNode(entries, start + leftLength, length - leftLength, token);
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

            var containsNode = area.Contains(node.Bounds);
            if (containsNode || node.Left is null)
            {
                var entries = node.Entries;
                for (var index = node.EntryStart; index < node.EntryStart + node.EntryLength; index++)
                {
                    var entry = entries[index];
                    if ((!excludePendingChanges || !_pendingChanges.ContainsKey(entry.EntityId)) &&
                        (containsNode || entry.Bounds.Intersects(area)))
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
        public BvhEntry[] Entries { get; }
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
            // Bulk loading keeps every subtree in one contiguous array range.
            Entries = left.Entries;
            EntryStart = left.EntryStart;
            EntryLength = left.EntryLength + right.EntryLength;
        }
    }
}
