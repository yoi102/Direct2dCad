using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetTextInvertedCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly bool _isInverted;
    private readonly Dictionary<EntityId, bool> _previousValues = [];

    public string Name => "Set Text Inverted";

    public SetTextInvertedCommand(EntityId entityId, bool isInverted)
        : this([entityId], isInverted)
    {
    }

    public SetTextInvertedCommand(IEnumerable<EntityId> entityIds, bool isInverted)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _isInverted = isInverted;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousValues.Clear();

        var entities = _entityIds
            .Select(document.GetEntity)
            .ToArray();

        foreach (var entity in entities)
            _previousValues[entity.Id] = GetInverted(entity);

        foreach (var entity in entities)
            SetInverted(entity, _isInverted);

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, isInverted) in _previousValues)
            SetInverted(document.GetEntity(entityId), isInverted);

        return CadDocumentChangeSet.ForEntities(_previousValues.Keys, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    private static bool GetInverted(CadEntity entity)
    {
        return entity switch
        {
            CadText text => text.IsInverted,
            CadShapeText text => text.IsInverted,
            _ => throw new NotSupportedException($"Entity type is not text: {entity.GetType().Name}")
        };
    }

    private static void SetInverted(CadEntity entity, bool isInverted)
    {
        switch (entity)
        {
            case CadText text:
                text.SetInverted(isInverted);
                break;
            case CadShapeText text:
                text.SetInverted(isInverted);
                break;
            default:
                throw new NotSupportedException($"Entity type is not text: {entity.GetType().Name}");
        }
    }
}
