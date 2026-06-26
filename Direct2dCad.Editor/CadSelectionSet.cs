using Direct2dCad.Db;

namespace Direct2dCad.Editor;

public sealed class CadSelectionSet
{
    private readonly HashSet<EntityId> _entityIds = [];

    public IReadOnlyCollection<EntityId> EntityIds => _entityIds.ToArray();
    public int Count => _entityIds.Count;

    public bool Contains(EntityId entityId) => _entityIds.Contains(entityId);

    public bool Add(EntityId entityId) => _entityIds.Add(entityId);

    public bool Remove(EntityId entityId) => _entityIds.Remove(entityId);

    public void Replace(IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        _entityIds.Clear();
        foreach (var entityId in entityIds)
            _entityIds.Add(entityId);
    }

    public void Clear() => _entityIds.Clear();
}
