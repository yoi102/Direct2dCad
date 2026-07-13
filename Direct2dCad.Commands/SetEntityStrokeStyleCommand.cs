using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetEntityStrokeStyleCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadStrokeStyle _strokeStyle;
    private readonly Dictionary<EntityId, CadStrokeStyle> _previousStrokeStyles = [];

    public string Name => "Set Entity Stroke Style";

    public SetEntityStrokeStyleCommand(IEnumerable<EntityId> entityIds, CadStrokeStyle strokeStyle)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _strokeStyle = strokeStyle;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousStrokeStyles.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousStrokeStyles[entityId] = entity.StrokeStyle;
            entity.SetStrokeStyle(_strokeStyle);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, strokeStyle) in _previousStrokeStyles)
            document.GetEntity(entityId).SetStrokeStyle(strokeStyle);

        return CadDocumentChangeSet.ForEntities(_previousStrokeStyles.Keys, CadEntityChangeKind.Appearance);
    }
}
