using Direct2dCad.Db;

namespace Direct2dCad.Editor;

public sealed class DirtySet
{
    private readonly Dictionary<EntityId, CadEntityChangeKind> _entityChanges = [];
    private bool _documentStructureChanged;
    private bool _viewSettingsChanged;

    public bool HasChanges => _entityChanges.Count > 0 || _documentStructureChanged || _viewSettingsChanged;

    public IReadOnlyCollection<EntityId> EntityIds => _entityChanges.Keys.ToArray();

    public void Add(CadDocumentChangeSet result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _documentStructureChanged |= result.AffectsDocumentStructure;
        _viewSettingsChanged |= result.AffectsViewSettings;

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

    public CadDocumentChangeSet Snapshot()
    {
        var result = new CadDocumentChangeSet(
            _entityChanges.Select(x => new CadEntityChange(x.Key, x.Value)));

        if (_documentStructureChanged)
            result = result.WithDocumentStructureChanged();

        if (_viewSettingsChanged)
            result = result.WithViewSettingsChanged();

        return result;
    }

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
        _viewSettingsChanged = false;
    }
}
