using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Rendering;

public interface ICadGeometryResourceManager
{
    void RebuildAll(CadDocument document);

    void ApplyChanges(CadDocument document, CadDocumentChangeSet changes);

    void RebuildEntity(CadDocument document, EntityId entityId);

    void RemoveEntity(EntityId entityId);
}
