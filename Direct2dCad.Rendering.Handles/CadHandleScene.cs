using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed partial class CadHandleScene
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
    public double MaximumScreenConstantSelectionStrokeWidth { get; private set; }
    public double MaximumWorldSelectionStrokeWidth { get; private set; }
    public bool IsEmpty => _items.Count == 0;

    public void Replace(IEnumerable<CadHandleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _geometryIndex = null;

        (_selectionReferenceItems, _previousSelectionReferenceItems) =
            (_previousSelectionReferenceItems, _selectionReferenceItems);
        if (items.TryGetNonEnumeratedCount(out var itemCount))
        {
            _items.EnsureCapacity(itemCount);
            _nonSelectionItems.EnsureCapacity(itemCount);
            _selectionReferenceItems.EnsureCapacity(itemCount);
            _selectionReferences.EnsureCapacity(itemCount);
        }

        _items.Clear();
        _nonSelectionItems.Clear();
        _selectionReferences.Clear();
        _selectionReferenceItems.Clear();
        HasTranslatedSelectionReferences = false;
        SelectionWorldBounds = CadRectD.Empty;
        MaximumScreenConstantSelectionStrokeWidth = 0;
        MaximumWorldSelectionStrokeWidth = 0;
        var selectionChanged = false;
        var selectionIndex = 0;
        foreach (var item in items)
        {
            if (item is null)
                continue;

            _items.Add(item);
            if (item is CadSelectionEntityReference reference)
            {
                _selectionReferences[reference.EntityId] = reference;
                _selectionReferenceItems.Add(reference);
                selectionChanged |=
                    selectionIndex >= _previousSelectionReferenceItems.Count ||
                    _previousSelectionReferenceItems[selectionIndex] != reference;
                selectionIndex++;
                HasTranslatedSelectionReferences |= reference.Offset != CadVectorD.Zero;
                SelectionWorldBounds = SelectionWorldBounds.Union(
                    reference.EntityBounds.Translate(reference.Offset));
                if (reference.Style.KeepSizeScreenConstant)
                {
                    MaximumScreenConstantSelectionStrokeWidth = Math.Max(
                        MaximumScreenConstantSelectionStrokeWidth,
                        Math.Max(0, reference.Style.StrokeWidth));
                }
                else
                {
                    MaximumWorldSelectionStrokeWidth = Math.Max(
                        MaximumWorldSelectionStrokeWidth,
                        Math.Max(0, reference.Style.StrokeWidth));
                }
            }
            else
            {
                _nonSelectionItems.Add(item);
            }
        }

        selectionChanged |= selectionIndex != _previousSelectionReferenceItems.Count;
        if (selectionChanged)
        {
            SelectionVersion = unchecked(SelectionVersion + 1);
        }
        else
        {
            // Keep the published list stable while incremental render-cache builders hold it.
            (_selectionReferenceItems, _previousSelectionReferenceItems) =
                (_previousSelectionReferenceItems, _selectionReferenceItems);
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
        _geometryIndex = null;
        if (_selectionReferenceItems.Count > 0)
            SelectionVersion = unchecked(SelectionVersion + 1);
        _items.Clear();
        _nonSelectionItems.Clear();
        _selectionReferences.Clear();
        _selectionReferenceItems.Clear();
        _previousSelectionReferenceItems.Clear();
        HasTranslatedSelectionReferences = false;
        SelectionWorldBounds = CadRectD.Empty;
        MaximumScreenConstantSelectionStrokeWidth = 0;
        MaximumWorldSelectionStrokeWidth = 0;
    }

}
