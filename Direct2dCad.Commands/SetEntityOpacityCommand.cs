using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetEntityOpacityCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly double _opacity;
    private readonly Dictionary<EntityId, double> _previousOpacities = [];

    public string Name => "Set Entity Opacity";

    public SetEntityOpacityCommand(IEnumerable<EntityId> entityIds, double opacity)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity));

        _opacity = Math.Clamp(opacity, 0.0, 1.0);
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousOpacities.Clear();

        var entities = new CadEntity[_entityIds.Length];
        for (var index = 0; index < _entityIds.Length; index++)
        {
            var entity = document.GetEntity(_entityIds[index]);
            entities[index] = entity;
            _previousOpacities[entity.Id] = GetOpacity(entity);
        }

        foreach (var entity in entities)
            SetOpacity(entity, _opacity);

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Opacity);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, opacity) in _previousOpacities)
            SetOpacity(document.GetEntity(entityId), opacity);

        return CadDocumentChangeSet.ForEntities(_previousOpacities.Keys, CadEntityChangeKind.Opacity);
    }

    private static double GetOpacity(CadEntity entity) => entity switch
    {
        CadImage image => image.Opacity,
        CadOleObject oleObject => oleObject.Opacity,
        _ => throw new NotSupportedException($"Entity type has no opacity: {entity.GetType().Name}")
    };

    private static void SetOpacity(CadEntity entity, double opacity)
    {
        switch (entity)
        {
            case CadImage image:
                image.SetOpacity(opacity);
                break;
            case CadOleObject oleObject:
                oleObject.SetOpacity(opacity);
                break;
            default:
                throw new NotSupportedException($"Entity type has no opacity: {entity.GetType().Name}");
        }
    }
}
