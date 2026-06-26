using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetEntityZIndexCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly int _zIndex;
    private readonly Dictionary<EntityId, int> _previousZIndexes = [];

    public string Name => "Set Entity ZIndex";

    public SetEntityZIndexCommand(IEnumerable<EntityId> entityIds, int zIndex)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _zIndex = zIndex;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousZIndexes.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousZIndexes[entityId] = entity.ZIndex;
            entity.SetZIndex(_zIndex);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.DrawOrder);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, zIndex) in _previousZIndexes)
            document.GetEntity(entityId).SetZIndex(zIndex);

        return CadDocumentChangeSet.ForEntities(_previousZIndexes.Keys, CadEntityChangeKind.DrawOrder);
    }
}
