using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public interface ICadSpatialIndex
{
    void Add(EntityId entityId, CadRectD bounds);

    void Remove(EntityId entityId);

    void Update(EntityId entityId, CadRectD bounds);

    IReadOnlyList<EntityId> Query(CadRectD area);

    void Clear();

    void Rebuild(CadDocument document);
}
