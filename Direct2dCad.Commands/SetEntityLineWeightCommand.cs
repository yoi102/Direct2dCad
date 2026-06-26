using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetEntityLineWeightCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadLineWeight? _lineWeight;
    private readonly Dictionary<EntityId, CadLineWeight?> _previousLineWeights = [];

    public string Name => "Set Entity Line Weight";

    public SetEntityLineWeightCommand(IEnumerable<EntityId> entityIds, CadLineWeight? lineWeight)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _lineWeight = lineWeight;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousLineWeights.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousLineWeights[entityId] = entity.LineWeight;
            entity.SetLineWeight(_lineWeight);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, lineWeight) in _previousLineWeights)
            document.GetEntity(entityId).SetLineWeight(lineWeight);

        return CadDocumentChangeSet.ForEntities(_previousLineWeights.Keys, CadEntityChangeKind.Appearance);
    }
}
