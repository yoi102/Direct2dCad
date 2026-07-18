using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed class CadHandleScene
{
    private readonly List<CadHandleItem> _items = [];
    private readonly List<CadHandleItem> _nonSelectionItems = [];
    private readonly Dictionary<EntityId, CadSelectionEntityReference> _selectionReferences = [];

    public IReadOnlyList<CadHandleItem> Items => _items;
    public IReadOnlyList<CadHandleItem> NonSelectionItems => _nonSelectionItems;
    public int SelectionReferenceCount => _selectionReferences.Count;
    public bool HasTranslatedSelectionReferences { get; private set; }
    public CadRectD SelectionWorldBounds { get; private set; } = CadRectD.Empty;
    public bool IsEmpty => _items.Count == 0;

    public void Replace(IEnumerable<CadHandleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Clear();
        foreach (var item in items)
        {
            if (item is null)
                continue;

            _items.Add(item);
            if (item is CadSelectionEntityReference reference)
            {
                _selectionReferences[reference.EntityId] = reference;
                HasTranslatedSelectionReferences |= reference.Offset != CadVectorD.Zero;
                SelectionWorldBounds = SelectionWorldBounds.Union(
                    reference.EntityBounds.Translate(reference.Offset));
            }
            else
            {
                _nonSelectionItems.Add(item);
            }
        }
    }

    public bool TryGetSelectionReference(
        EntityId entityId,
        out CadSelectionEntityReference? reference)
    {
        return _selectionReferences.TryGetValue(entityId, out reference);
    }

    public void Clear()
    {
        _items.Clear();
        _nonSelectionItems.Clear();
        _selectionReferences.Clear();
        HasTranslatedSelectionReferences = false;
        SelectionWorldBounds = CadRectD.Empty;
    }
}
