using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class DeleteEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly Dictionary<EntityId, bool> _previousErasedStates = [];

    public string Name => "Delete Entities";

    public DeleteEntitiesCommand(IEnumerable<EntityId> entityIds)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousErasedStates.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousErasedStates[entityId] = entity.IsErased;
            entity.Erase();
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, wasErased) in _previousErasedStates)
        {
            var entity = document.GetEntity(entityId);
            if (wasErased)
                entity.Erase();
            else
                entity.Restore();
        }

        return CadDocumentChangeSet.ForEntities(_previousErasedStates.Keys, CadEntityChangeKind.Visibility);
    }
}
