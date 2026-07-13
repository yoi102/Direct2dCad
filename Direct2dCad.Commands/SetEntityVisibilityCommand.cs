using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class SetEntityVisibilityCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly bool _isVisible;
    private readonly Dictionary<EntityId, bool> _previousVisibility = [];

    public string Name => "Set Entity Visibility";

    public SetEntityVisibilityCommand(IEnumerable<EntityId> entityIds, bool isVisible)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _isVisible = isVisible;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousVisibility.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousVisibility[entityId] = entity.IsVisible;
            entity.SetVisible(_isVisible);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Visibility);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, wasVisible) in _previousVisibility)
            document.GetEntity(entityId).SetVisible(wasVisible);

        return CadDocumentChangeSet.ForEntities(_previousVisibility.Keys, CadEntityChangeKind.Visibility);
    }
}
