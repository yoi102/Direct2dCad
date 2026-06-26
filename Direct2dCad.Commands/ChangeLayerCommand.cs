using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class ChangeLayerCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly LayerId _targetLayerId;
    private readonly Dictionary<EntityId, LayerId> _previousLayers = [];

    public string Name => "Change Layer";

    public ChangeLayerCommand(IEnumerable<EntityId> entityIds, LayerId targetLayerId)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _targetLayerId = targetLayerId;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousLayers.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousLayers[entityId] = entity.LayerId;
            document.ChangeEntityLayer(entityId, _targetLayerId);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Layer | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, layerId) in _previousLayers)
            document.ChangeEntityLayer(entityId, layerId);

        return CadDocumentChangeSet.ForEntities(_previousLayers.Keys, CadEntityChangeKind.Layer | CadEntityChangeKind.Appearance);
    }
}
