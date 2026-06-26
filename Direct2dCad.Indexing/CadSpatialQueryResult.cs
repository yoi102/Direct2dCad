using Direct2dCad.Db;

namespace Direct2dCad.Indexing;

public sealed class CadSpatialQueryResult
{
    public IReadOnlyList<EntityId> EntityIds { get; }

    public CadSpatialQueryResult(IEnumerable<EntityId> entityIds)
    {
        EntityIds = entityIds?.ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
    }
}
