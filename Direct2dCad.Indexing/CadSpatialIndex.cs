using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public sealed class CadSpatialIndex : ICadSpatialIndex
{
    private readonly Dictionary<EntityId, CadRectD> _boundsByEntity = [];

    public int Count => _boundsByEntity.Count;

    public void Add(EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            return;

        _boundsByEntity[entityId] = bounds;
    }

    public void Remove(EntityId entityId)
    {
        _boundsByEntity.Remove(entityId);
    }

    public void Update(EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            Remove(entityId);
        else
            _boundsByEntity[entityId] = bounds;
    }

    public IReadOnlyList<EntityId> Query(CadRectD area)
    {
        if (area.IsEmpty)
            return [];

        return _boundsByEntity
            .Where(x => x.Value.Intersects(area))
            .Select(x => x.Key)
            .ToArray();
    }

    public void Clear()
    {
        _boundsByEntity.Clear();
    }

    public void Rebuild(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Clear();
        foreach (var entity in document.Entities.Values)
        {
            if (!entity.IsErased && entity.IsVisible)
                Add(entity.Id, entity.Bounds);
        }
    }
}
