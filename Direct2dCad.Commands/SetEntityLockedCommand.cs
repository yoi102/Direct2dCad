using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetEntityLockedCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly bool _isLocked;
    private readonly Dictionary<EntityId, bool> _previousValues = [];

    public SetEntityLockedCommand(IEnumerable<EntityId> entityIds, bool isLocked)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));

        _isLocked = isLocked;
    }

    public string Name => "Set Entity Locked";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousValues.Clear();

        var entities = _entityIds.Select(document.GetEntity).ToArray();
        foreach (var entity in entities)
        {
            EnsureLayerAllowsStateChange(document, entity);
            _previousValues[entity.Id] = entity.IsLocked;
        }

        foreach (var entity in entities)
            entity.SetLocked(_isLocked);

        return ChangeSet(_entityIds);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var (entityId, isLocked) in _previousValues)
            document.GetEntity(entityId).SetLocked(isLocked);

        return ChangeSet(_previousValues.Keys);
    }

    private static void EnsureLayerAllowsStateChange(CadDocument document, CadEntity entity)
    {
        if (entity.IsErased)
            throw new InvalidOperationException($"Entity cannot be edited: {entity.Id}");

        var layer = document.GetLayer(entity.LayerId);
        if (layer.IsLocked)
            throw new InvalidOperationException($"Layer is locked: {layer.Name}");
        if (layer.IsFrozen)
            throw new InvalidOperationException($"Layer is frozen: {layer.Name}");
    }

    private static CadDocumentChangeSet ChangeSet(IEnumerable<EntityId> entityIds) =>
        CadDocumentChangeSet.ForEntities(entityIds, CadEntityChangeKind.Metadata);
}
