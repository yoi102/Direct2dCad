using Direct2dCad.Db;

namespace Direct2dCad.Editor;

public sealed class DirtySet
{
    private readonly Dictionary<EntityId, CadEntityChangeKind> _entityChanges = [];
    private bool _documentStructureChanged;
    private bool _layoutsChanged;
    private bool _layoutStructureChanged;
    private bool _viewSettingsChanged;
    private CadDocumentTableChangeKind _tableChanges;

    public bool HasChanges =>
        _tableChanges != CadDocumentTableChangeKind.None ||
        _entityChanges.Count > 0 ||
        _documentStructureChanged ||
        _layoutsChanged ||
        _layoutStructureChanged ||
        _viewSettingsChanged;

    public IReadOnlyCollection<EntityId> EntityIds => _entityChanges.Keys;

    public void Add(CadDocumentChangeSet result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _documentStructureChanged |= result.AffectsDocumentStructure;
        _layoutsChanged |= result.AffectsLayouts;
        _layoutStructureChanged |= result.AffectsLayoutStructure;
        _viewSettingsChanged |= result.AffectsViewSettings;
        _tableChanges |= result.TableChanges;

        foreach (var change in result.EntityChanges)
        {
            _entityChanges.TryGetValue(change.EntityId, out var existing);
            _entityChanges[change.EntityId] = existing | change.Kind;
        }
    }

    public void Add(EntityId entityId, CadEntityChangeKind kind)
    {
        _entityChanges.TryGetValue(entityId, out var existing);
        _entityChanges[entityId] = existing | kind;
    }

    public CadDocumentChangeSet Snapshot() => new(
        _entityChanges.Select(x => new CadEntityChange(x.Key, x.Value)))
    {
        AffectsDocumentStructure = _documentStructureChanged,
        AffectsLayouts = _layoutsChanged || _layoutStructureChanged,
        AffectsLayoutStructure = _layoutStructureChanged,
        AffectsViewSettings = _viewSettingsChanged,
        TableChanges = _tableChanges
    };

    public CadDocumentChangeSet Drain()
    {
        var snapshot = Snapshot();
        Clear();
        return snapshot;
    }

    public void Clear()
    {
        _entityChanges.Clear();
        _documentStructureChanged = false;
        _layoutsChanged = false;
        _layoutStructureChanged = false;
        _viewSettingsChanged = false;
        _tableChanges = CadDocumentTableChangeKind.None;
    }
}
