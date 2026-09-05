using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed partial class CadHandleScene
{
    private CadSelectionGeometryIndex? _geometryIndex;

    /// <summary>Updates bounds without rebuilding selection order or individual grips.</summary>
    public bool TryUpdateGeometry(CadDocument document, IReadOnlyCollection<EntityId> changedIds,
        bool includeLockedGrips = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changedIds);
        if (HasTranslatedSelectionReferences || _selectionReferenceItems.Count == 0 ||
            changedIds.Count > Math.Max(64, _selectionReferenceItems.Count / 4) ||
            _nonSelectionItems.Count > 1 ||
            (_nonSelectionItems.Count == 1 && _nonSelectionItems[0] is not CadGripHandle { Type: CadHandleType.Center }))
            return false;

        // Membership, visibility, locks, styles and order must use Replace instead.
        foreach (var id in changedIds)
            if (!_selectionReferences.ContainsKey(id) ||
                !document.TryGetEntity(id, out var entity) || entity is not { IsErased: false, IsVisible: true } ||
                entity.Bounds.IsEmpty)
                return false;

        _geometryIndex ??= new CadSelectionGeometryIndex(document, _items, _selectionReferenceItems, includeLockedGrips);
        var changed = false;
        foreach (var id in changedIds)
        {
            var previous = _selectionReferences[id];
            var bounds = document.GetEntity(id).Bounds;
            if (previous.EntityBounds == bounds)
                continue;
            var reference = previous with { EntityBounds = bounds };
            var indices = _geometryIndex.Update(id, bounds);
            _selectionReferences[id] = reference;
            _selectionReferenceItems[indices.Selection] = reference;
            _items[indices.Item] = reference;
            changed = true;
        }
        if (!changed)
            return true;

        SelectionWorldBounds = _geometryIndex.SelectionBounds;
        if (_nonSelectionItems.Count == 1)
        {
            var grip = (CadGripHandle)_nonSelectionItems[0];
            if (!_geometryIndex.MoveBounds.IsEmpty)
            {
                var updated = grip with { Position = _geometryIndex.MoveBounds.Center };
                _nonSelectionItems[0] = updated;
                _items[_geometryIndex.GripItemIndex] = updated;
            }
        }
        SelectionVersion = unchecked(SelectionVersion + 1);
        return true;
    }
}

// Built on the first incremental edit, then maintained in O(log selection count).
internal sealed class CadSelectionGeometryIndex
{
    private readonly Dictionary<EntityId, (int Selection, int Item, bool Movable)> _indices = [];
    private readonly CadRectD[] _bounds;
    private readonly CadRectD[] _moveBounds;
    private readonly int _leafCount;
    public int GripItemIndex { get; }
    public CadRectD SelectionBounds => _bounds[1];
    public CadRectD MoveBounds => _moveBounds[1];

    public CadSelectionGeometryIndex(CadDocument document, IReadOnlyList<CadHandleItem> items,
        IReadOnlyList<CadSelectionEntityReference> references, bool includeLocked)
    {
        _leafCount = 1;
        while (_leafCount < references.Count)
            _leafCount *= 2;
        _bounds = new CadRectD[_leafCount * 2];
        _moveBounds = new CadRectD[_leafCount * 2];
        Array.Fill(_bounds, CadRectD.Empty);
        Array.Fill(_moveBounds, CadRectD.Empty);
        var selectionIndex = 0;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (items[itemIndex] is not CadSelectionEntityReference reference)
            {
                GripItemIndex = itemIndex;
                continue;
            }
            var entity = document.GetEntity(reference.EntityId);
            var locked = entity.IsLocked || document.GetLayer(entity.LayerId).IsLocked;
            var movable = (includeLocked || !locked) && CadHandleSceneBuilder.SupportsCenterGrip(entity);
            _indices.Add(reference.EntityId, (selectionIndex, itemIndex, movable));
            _bounds[_leafCount + selectionIndex] = reference.EntityBounds;
            if (movable)
                _moveBounds[_leafCount + selectionIndex] = reference.EntityBounds;
            selectionIndex++;
        }
        for (var i = _leafCount - 1; i > 0; i--)
            Merge(i);
    }

    public (int Selection, int Item) Update(EntityId id, CadRectD bounds)
    {
        var index = _indices[id];
        var leaf = _leafCount + index.Selection;
        _bounds[leaf] = bounds;
        _moveBounds[leaf] = index.Movable ? bounds : CadRectD.Empty;
        for (var parent = leaf / 2; parent > 0; parent /= 2)
            Merge(parent);
        return (index.Selection, index.Item);
    }

    private void Merge(int index)
    {
        _bounds[index] = _bounds[index * 2].Union(_bounds[index * 2 + 1]);
        _moveBounds[index] = _moveBounds[index * 2].Union(_moveBounds[index * 2 + 1]);
    }
}
