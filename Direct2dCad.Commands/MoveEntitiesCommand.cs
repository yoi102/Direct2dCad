using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class MoveEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadVectorD _delta;

    public string Name => "Move Entities";

    public MoveEntitiesCommand(IEnumerable<EntityId> entityIds, CadVectorD delta)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _delta = delta;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        Move(document, _delta);
        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        Move(document, -_delta);
        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Geometry);
    }

    private void Move(CadDocument document, CadVectorD delta)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            CadEntityTransform.Translate(entity, delta);
        }
    }
}
