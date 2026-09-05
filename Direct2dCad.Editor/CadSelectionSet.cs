using Direct2dCad.Db;

namespace Direct2dCad.Editor;

public sealed class CadSelectionSet
{
    private readonly HashSet<EntityId> _entityIds = [];

    public IReadOnlySet<EntityId> EntityIds => _entityIds;
    public int Count => _entityIds.Count;
    public long Version { get; private set; }

    public bool Contains(EntityId entityId) => _entityIds.Contains(entityId);

    public bool Add(EntityId entityId)
    {
        if (!_entityIds.Add(entityId))
            return false;

        IncrementVersion();
        return true;
    }

    public bool Remove(EntityId entityId)
    {
        if (!_entityIds.Remove(entityId))
            return false;

        IncrementVersion();
        return true;
    }

    public bool RemoveWhere(Predicate<EntityId> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var count = _entityIds.Count;
        try
        {
            return _entityIds.RemoveWhere(predicate) > 0;
        }
        finally
        {
            if (_entityIds.Count != count)
                IncrementVersion();
        }
    }

    public void Replace(IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        if (ReferenceEquals(entityIds, _entityIds))
            return;

        var replacement = entityIds.ToHashSet();
        if (_entityIds.SetEquals(replacement))
            return;

        _entityIds.Clear();
        _entityIds.UnionWith(replacement);
        IncrementVersion();
    }

    public void Clear()
    {
        if (_entityIds.Count == 0)
            return;

        _entityIds.Clear();
        IncrementVersion();
    }

    private void IncrementVersion() => Version = unchecked(Version + 1);
}
