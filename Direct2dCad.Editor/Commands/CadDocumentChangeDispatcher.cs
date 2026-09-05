using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Commands;

public sealed class CadDocumentChangeDispatcher
{
    private readonly CadDocument _document;
    private readonly DirtySet _dirtySet;
    private readonly ICadSpatialIndex? _spatialIndex;
    private readonly List<ICadGeometryResourceManager> _resourceManagers = [];
    private readonly DirtySet _pendingUpdates = new();
    private bool _updatesDeferred;

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
        UpdateSpatialIndex(result);
        if (_updatesDeferred)
        {
            _pendingUpdates.Add(result);
            return;
        }
        PublishUpdates(result);
    }

    internal IDisposable DeferUpdates()
    {
        if (_updatesDeferred)
            throw new InvalidOperationException("Document updates are already deferred.");
        _updatesDeferred = true;
        return new UpdateScope(this);
    }

    private void FlushUpdates()
    {
        _updatesDeferred = false;
        if (!_pendingUpdates.HasChanges)
            return;

        var pending = _pendingUpdates.Drain();
        // A rollback can produce both Created and Deleted. Consumers must see the
        // final lifetime state, not remove a resource for an entity restored by undo.
        var changes = new CadDocumentChangeSet(pending.EntityChanges.Select(change =>
        {
            const CadEntityChangeKind lifetime = CadEntityChangeKind.Created | CadEntityChangeKind.Deleted;
            if ((change.Kind & lifetime) == 0)
                return change;
            var alive = _document.TryGetEntity(change.EntityId, out var entity) && entity is { IsErased: false };
            return change with { Kind = (change.Kind & ~lifetime) |
                (alive ? CadEntityChangeKind.Created : CadEntityChangeKind.Deleted) };
        }))
        {
            TableChanges = pending.TableChanges,
            AffectsDocumentStructure = pending.AffectsDocumentStructure,
            AffectsLayouts = pending.AffectsLayouts,
            AffectsLayoutStructure = pending.AffectsLayoutStructure,
            AffectsViewSettings = pending.AffectsViewSettings
        };
        PublishUpdates(changes);
    }

    private void PublishUpdates(CadDocumentChangeSet result)
    {
        _dirtySet.Add(result);
        UpdateGeometryResources(result);
        DocumentChanged?.Invoke(this, result);
    }

    private sealed class UpdateScope(CadDocumentChangeDispatcher owner) : IDisposable
    {
        private CadDocumentChangeDispatcher? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.FlushUpdates();
    }

    private CadDocumentChangeSet ExpandBlockReferenceChanges(CadDocumentChangeSet result)
    {
        var affectedReferenceIds = ResolveAffectedBlockReferenceIds(result);
        var appearanceReferences = ResolveAppearanceReferences(result);
        if (affectedReferenceIds.Count == 0 && appearanceReferences is null)
            return result;

        var changes = new Dictionary<EntityId, CadEntityChangeKind>(
            result.EntityChanges.Count);
        foreach (var change in result.EntityChanges)
        {
            changes[change.EntityId] =
                changes.GetValueOrDefault(change.EntityId) | change.Kind;
        }
        foreach (var entityId in affectedReferenceIds)
        {
            changes[entityId] = changes.GetValueOrDefault(entityId) |
                                CadEntityChangeKind.Geometry;
        }
        if (appearanceReferences is not null)
            foreach (var (entityId, kind) in appearanceReferences)
                changes[entityId] = changes.GetValueOrDefault(entityId) | kind;

        return new CadDocumentChangeSet(
            changes.Select(change => new CadEntityChange(change.Key, change.Value)))
        {
            AffectsDocumentStructure = result.AffectsDocumentStructure,
            TableChanges = result.TableChanges,
            AffectsLayouts = result.AffectsLayouts,
            AffectsLayoutStructure = result.AffectsLayoutStructure,
            AffectsViewSettings = result.AffectsViewSettings
        };
    }

    private IReadOnlyList<EntityId> ResolveAffectedBlockReferenceIds(
        CadDocumentChangeSet result)
    {
        if (result.HasResolvedBlockReferenceChanges && !result.AffectsDocumentStructure)
            return [];

        if (result.AffectsDocumentStructure)
            return _document.RefreshBlockReferenceBounds();

        const CadEntityChangeKind relevantChanges =
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.EmbeddedData |
            CadEntityChangeKind.Rotation;
        List<EntityId>? changedEntityIds = null;
        foreach (var change in result.EntityChanges)
        {
            if ((change.Kind & relevantChanges) == 0)
                continue;

            if (!_document.TryGetEntity(change.EntityId, out var entity) || entity is null)
                return _document.RefreshBlockReferenceBounds();

            var isDeleted = change.Kind.HasFlag(CadEntityChangeKind.Deleted);
            if ((!isDeleted && entity is CadBlockReference) ||
                _document.IsBlockReferenced(entity.OwnerBlockId))
                (changedEntityIds ??= []).Add(change.EntityId);
        }

        return changedEntityIds is null
            ? []
            : _document.RefreshAffectedBlockReferenceBounds(changedEntityIds);
    }

    private Dictionary<EntityId, CadEntityChangeKind>? ResolveAppearanceReferences(CadDocumentChangeSet result)
    {
        const CadEntityChangeKind appearance = CadEntityChangeKind.Appearance | CadEntityChangeKind.Layer |
            CadEntityChangeKind.DrawOrder | CadEntityChangeKind.Fill | CadEntityChangeKind.Opacity;
        Queue<(BlockId Owner, CadEntityChangeKind Kind)>? pending = null;
        foreach (var change in result.EntityChanges)
        {
            if ((change.Kind & appearance) == 0 ||
                !_document.TryGetEntity(change.EntityId, out var entity) || entity is null ||
                !_document.IsBlockReferenced(entity.OwnerBlockId))
                continue;
            (pending ??= new()).Enqueue((entity.OwnerBlockId,
                CadEntityChangeKind.Appearance | (change.Kind & CadEntityChangeKind.Fill)));
        }
        if (pending is null)
            return null;

        var references = new Dictionary<EntityId, CadEntityChangeKind>();
        var propagated = new Dictionary<BlockId, CadEntityChangeKind>();
        while (pending.TryDequeue(out var item))
        {
            var missing = item.Kind & ~propagated.GetValueOrDefault(item.Owner);
            if (missing == CadEntityChangeKind.None)
                continue;
            propagated[item.Owner] = propagated.GetValueOrDefault(item.Owner) | missing;
            foreach (var id in _document.GetBlockReferenceIds(item.Owner))
            {
                if (!_document.TryGetEntity(id, out var reference) || reference is not { IsErased: false })
                    continue;
                references[id] = references.GetValueOrDefault(id) | missing;
                pending.Enqueue((reference.OwnerBlockId, missing));
            }
        }
        return references;
    }

    private void UpdateGeometryResources(CadDocumentChangeSet result)
    {
        for (var index = 0; index < _resourceManagers.Count; index++)
            _resourceManagers[index].ApplyChanges(_document, result);
    }

    private void UpdateSpatialIndex(CadDocumentChangeSet result)
    {
        if (_spatialIndex is null)
            return;

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
                _spatialIndex.Update(entity.Id, entity.OwnerBlockId, entity.Bounds);
        }
    }
}
