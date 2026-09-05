using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Rendering.Handles;

/// <summary>
/// Reusable storage for building large handle scenes without allocating new backing arrays.
/// </summary>
public sealed class CadHandleSceneBuildBuffer
{
    internal List<CadHandleItem> Items { get; } = [];
    internal List<CadEntity> SelectedEntities { get; } = [];
    private readonly Dictionary<EntityId, int> _insertionIndices = [];
    private readonly Dictionary<BlockId, int> _ownerCounts = [];
    private CadDocument? _document;
    private CadHandleSelectionCacheKey? _cacheKey;
    private readonly Comparison<CadEntity> _comparison;

    public CadHandleSceneBuildBuffer() => _comparison = Compare;

    internal bool IsOrderCurrent(CadDocument document, CadHandleSelectionCacheKey? key) =>
        key.HasValue && ReferenceEquals(document, _document) && _cacheKey == key;

    internal void InvalidateOrder() => _cacheKey = null;

    internal void Sort(CadDocument document, CadHandleSelectionCacheKey? key)
    {
        _document = document;
        _cacheKey = null;
        _insertionIndices.Clear();
        _ownerCounts.Clear();
        foreach (var entity in SelectedEntities)
        {
            _insertionIndices[entity.Id] = int.MaxValue;
            _ownerCounts[entity.OwnerBlockId] = _ownerCounts.GetValueOrDefault(entity.OwnerBlockId) + 1;
        }
        foreach (var (ownerId, count) in _ownerCounts)
        {
            if (!document.TryGetBlock(ownerId, out var owner) || owner is null)
                continue;
            var remaining = count;
            for (var index = 0; index < owner.EntityIds.Count && remaining > 0; index++)
            {
                var id = owner.EntityIds[index];
                if (!_insertionIndices.ContainsKey(id))
                    continue;
                _insertionIndices[id] = index;
                remaining--;
            }
        }
        SelectedEntities.Sort(_comparison);
        _cacheKey = key;
    }

    public void Clear()
    {
        Items.Clear();
        SelectedEntities.Clear();
        _insertionIndices.Clear();
        _ownerCounts.Clear();
        _document = null;
        _cacheKey = null;
    }

    private int Compare(CadEntity left, CadEntity right)
    {
        var priorities = _document!.DocumentSettings.LayerDrawingPriority;
        var result = priorities.GetPriority(left.LayerId).CompareTo(priorities.GetPriority(right.LayerId));
        if (result != 0)
            return result;
        result = left.ZIndex.CompareTo(right.ZIndex);
        if (result != 0)
            return result;
        result = _insertionIndices[left.Id].CompareTo(_insertionIndices[right.Id]);
        return result != 0 ? result : left.Id.Value.CompareTo(right.Id.Value);
    }
}

/// <summary>
/// Opt-in ordering reuse. Change DocumentVersion for every document mutation,
/// including visibility, layer/order changes and entity replacement.
/// </summary>
public readonly record struct CadHandleSelectionCacheKey(long SelectionVersion, long DocumentVersion);
