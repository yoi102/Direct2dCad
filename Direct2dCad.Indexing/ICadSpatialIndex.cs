using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public interface ICadSpatialIndex
{
    void Add(EntityId entityId, CadRectD bounds);

    void Add(EntityId entityId, BlockId ownerBlockId, CadRectD bounds);

    void Remove(EntityId entityId);

    void Update(EntityId entityId, CadRectD bounds);

    void Update(EntityId entityId, BlockId ownerBlockId, CadRectD bounds);

    IReadOnlyList<EntityId> Query(CadRectD area);

    IReadOnlyList<EntityId> Query(BlockId ownerBlockId, CadRectD area);

    void Clear();

    void Rebuild(CadDocument document);
}
