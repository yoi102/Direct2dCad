using Direct2dCad.Db.Cad;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Commands;

public sealed class CadDocumentChangeDispatcher
{
    private readonly CadDocument _document;
    private readonly DirtySet _dirtySet;
    private readonly ICadSpatialIndex? _spatialIndex;
    private readonly List<ICadGeometryResourceManager> _resourceManagers = [];

    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;

    public CadDocumentChangeDispatcher(
        CadDocument document,
        DirtySet dirtySet,
        ICadSpatialIndex? spatialIndex = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _dirtySet = dirtySet ?? throw new ArgumentNullException(nameof(dirtySet));
        _spatialIndex = spatialIndex;
    }

    public CadDocumentChangeSet DrainDirtyChanges() => _dirtySet.Drain();

    public void RegisterGeometryResourceManager(
        ICadGeometryResourceManager resourceManager,
        bool rebuildExistingResources = true)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        if (_resourceManagers.Contains(resourceManager))
            return;

        _resourceManagers.Add(resourceManager);

        if (rebuildExistingResources)
            resourceManager.RebuildAll(_document);
    }

    public bool UnregisterGeometryResourceManager(ICadGeometryResourceManager resourceManager)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        return _resourceManagers.Remove(resourceManager);
    }

    public void RegisterRenderer(ICadRenderer renderer, bool rebuildExistingResources = true)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        if (renderer is not ICadGeometryResourceManager resourceManager)
            throw new ArgumentException(
                "Renderer must implement ICadGeometryResourceManager to receive document resource updates.",
                nameof(renderer));

        RegisterGeometryResourceManager(resourceManager, rebuildExistingResources);
    }

    public bool UnregisterRenderer(ICadRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer is ICadGeometryResourceManager resourceManager &&
               UnregisterGeometryResourceManager(resourceManager);
    }

    public void Publish(CadDocumentChangeSet result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.DocumentChanged)
            return;

        result = ExpandBlockReferenceChanges(result);
        _dirtySet.Add(result);
        UpdateSpatialIndex(result);
        UpdateGeometryResources(result);
        DocumentChanged?.Invoke(this, result);
    }

    private CadDocumentChangeSet ExpandBlockReferenceChanges(CadDocumentChangeSet result)
    {
        var affectedReferenceIds = _document.RefreshBlockReferenceBounds();
        if (affectedReferenceIds.Count == 0)
            return result;

        var changes = result.EntityChanges
            .GroupBy(change => change.EntityId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(CadEntityChangeKind.None, (kind, change) => kind | change.Kind));
        foreach (var entityId in affectedReferenceIds)
        {
            changes[entityId] = changes.GetValueOrDefault(entityId) |
                                CadEntityChangeKind.Geometry;
        }

        return new CadDocumentChangeSet(
            changes.Select(change => new CadEntityChange(change.Key, change.Value)))
        {
            AffectsDocumentStructure = result.AffectsDocumentStructure,
            AffectsLayouts = result.AffectsLayouts,
            AffectsLayoutStructure = result.AffectsLayoutStructure,
            AffectsViewSettings = result.AffectsViewSettings
        };
    }

    private void UpdateGeometryResources(CadDocumentChangeSet result)
    {
        foreach (var resourceManager in _resourceManagers.ToArray())
            resourceManager.ApplyChanges(_document, result);
    }

    private void UpdateSpatialIndex(CadDocumentChangeSet result)
    {
        if (_spatialIndex is null)
            return;

        if (result.AffectsDocumentStructure)
        {
            _spatialIndex.Clear();
            foreach (var entity in _document.Entities.Values)
            {
                if (!entity.IsErased && entity.IsVisible)
                    _spatialIndex.Update(entity.Id, entity.Bounds);
            }
            return;
        }

        foreach (var change in result.EntityChanges)
        {
            if (!_document.TryGetEntity(change.EntityId, out var entity) || entity is null)
            {
                _spatialIndex.Remove(change.EntityId);
                continue;
            }

            if (entity.IsErased || !entity.IsVisible || change.Kind.HasFlag(CadEntityChangeKind.Deleted))
                _spatialIndex.Remove(entity.Id);
            else if (change.Kind.HasFlag(CadEntityChangeKind.Geometry) ||
                     change.Kind.HasFlag(CadEntityChangeKind.Rotation) ||
                     change.Kind.HasFlag(CadEntityChangeKind.Created) ||
                     change.Kind.HasFlag(CadEntityChangeKind.Visibility) ||
                     change.Kind.HasFlag(CadEntityChangeKind.Layer))
                _spatialIndex.Update(entity.Id, entity.Bounds);
        }
    }
}
