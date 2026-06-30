using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetTextInvertedMarginFactorCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly double _marginFactor;
    private readonly Dictionary<EntityId, double> _previousValues = [];

    public string Name => "Set Text Inverted Margin";

    public SetTextInvertedMarginFactorCommand(EntityId entityId, double marginFactor)
        : this([entityId], marginFactor)
    {
    }

    public SetTextInvertedMarginFactorCommand(IEnumerable<EntityId> entityIds, double marginFactor)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _marginFactor = GuardMarginFactor(marginFactor);

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
            _previousValues[entity.Id] = GetMarginFactor(entity);

        foreach (var entity in entities)
            SetMarginFactor(entity, _marginFactor);

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, marginFactor) in _previousValues)
            SetMarginFactor(document.GetEntity(entityId), marginFactor);

        return CadDocumentChangeSet.ForEntities(_previousValues.Keys, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    private static double GetMarginFactor(CadEntity entity)
    {
        return entity switch
        {
            CadText text => text.InvertedMarginFactor,
            CadShapeText text => text.InvertedMarginFactor,
            _ => throw new NotSupportedException($"Entity type is not text: {entity.GetType().Name}")
        };
    }

    private static void SetMarginFactor(CadEntity entity, double marginFactor)
    {
        switch (entity)
        {
            case CadText text:
                text.SetInvertedMarginFactor(marginFactor);
                break;
            case CadShapeText text:
                text.SetInvertedMarginFactor(marginFactor);
                break;
            default:
                throw new NotSupportedException($"Entity type is not text: {entity.GetType().Name}");
        }
    }

    private static double GuardMarginFactor(double marginFactor)
    {
        return marginFactor < 0 || double.IsNaN(marginFactor) || double.IsInfinity(marginFactor)
            ? throw new ArgumentOutOfRangeException(nameof(marginFactor))
            : marginFactor;
    }
}
