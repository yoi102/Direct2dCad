using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

/// <summary>
/// Changes the edit lock state of one or more entities.
/// Unlocking is intentionally allowed even when the entity is currently locked;
/// otherwise a locked entity could never be unlocked through the command system.
/// </summary>
public sealed class SetEntityLockedCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly bool _isLocked;
    private readonly Dictionary<EntityId, bool> _previousStates = [];

    public string Name => "Set Entity Locked";

    public SetEntityLockedCommand(IEnumerable<EntityId> entityIds, bool isLocked)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _isLocked = isLocked;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousStates.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            if (entity.IsErased)
                throw new InvalidOperationException($"Entity is erased: {entityId}");

            if (_isLocked && !entity.IsLocked)
                CadEntityAccessPolicy.EnsureEditable(document, entity);

            _previousStates[entityId] = entity.IsLocked;
        }

        foreach (var entityId in _entityIds)
            document.GetEntity(entityId).SetLocked(_isLocked);

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Metadata);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, isLocked) in _previousStates)
            document.GetEntity(entityId).SetLocked(isLocked);

        return CadDocumentChangeSet.ForEntities(_previousStates.Keys, CadEntityChangeKind.Metadata);
    }
}
