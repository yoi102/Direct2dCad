using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed class CadHandleScene
{
    private readonly List<CadHandleItem> _items = [];
    private readonly List<CadHandleItem> _nonSelectionItems = [];
    private readonly Dictionary<EntityId, CadSelectionEntityReference> _selectionReferences = [];
    private List<CadSelectionEntityReference> _selectionReferenceItems = [];
    private List<CadSelectionEntityReference> _previousSelectionReferenceItems = [];

    public IReadOnlyList<CadHandleItem> Items => _items;
    public IReadOnlyList<CadHandleItem> NonSelectionItems => _nonSelectionItems;
    public IReadOnlyList<CadSelectionEntityReference> SelectionReferences => _selectionReferenceItems;
    public int SelectionReferenceCount => _selectionReferences.Count;
    public long SelectionVersion { get; private set; }
    public bool HasTranslatedSelectionReferences { get; private set; }
    public CadRectD SelectionWorldBounds { get; private set; } = CadRectD.Empty;
    public bool IsEmpty => _items.Count == 0;

    public void Replace(IEnumerable<CadHandleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        (_selectionReferenceItems, _previousSelectionReferenceItems) =
            (_previousSelectionReferenceItems, _selectionReferenceItems);
        _items.Clear();
        _nonSelectionItems.Clear();
        _selectionReferences.Clear();
        _selectionReferenceItems.Clear();
        HasTranslatedSelectionReferences = false;
        SelectionWorldBounds = CadRectD.Empty;
        foreach (var item in items)
        {
            if (item is null)
                continue;

            _items.Add(item);
            if (item is CadSelectionEntityReference reference)
            {
                _selectionReferences[reference.EntityId] = reference;
                _selectionReferenceItems.Add(reference);
                HasTranslatedSelectionReferences |= reference.Offset != CadVectorD.Zero;
                SelectionWorldBounds = SelectionWorldBounds.Union(
                    reference.EntityBounds.Translate(reference.Offset));
            }
            else
            {
                _nonSelectionItems.Add(item);
            }
        }

        if (!SelectionReferencesEqual(
                _previousSelectionReferenceItems,
                _selectionReferenceItems))
        {
            SelectionVersion = unchecked(SelectionVersion + 1);
        }
        _previousSelectionReferenceItems.Clear();
    }

    public bool TryGetSelectionReference(
        EntityId entityId,
        out CadSelectionEntityReference? reference)
    {
        return _selectionReferences.TryGetValue(entityId, out reference);
    }

    public void Clear()
    {
        if (_selectionReferenceItems.Count > 0)
            SelectionVersion = unchecked(SelectionVersion + 1);
        _items.Clear();
        _nonSelectionItems.Clear();
        _selectionReferences.Clear();
        _selectionReferenceItems.Clear();
        _previousSelectionReferenceItems.Clear();
        HasTranslatedSelectionReferences = false;
        SelectionWorldBounds = CadRectD.Empty;
    }

    private static bool SelectionReferencesEqual(
        IReadOnlyList<CadSelectionEntityReference> left,
        IReadOnlyList<CadSelectionEntityReference> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }
}
