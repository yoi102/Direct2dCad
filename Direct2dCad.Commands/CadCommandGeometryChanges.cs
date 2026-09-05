using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

internal static class CadCommandGeometryChanges
{
    public static CadDocumentChangeSet Resolve(
        CadDocument document, IReadOnlyList<EntityId> entityIds, CadEntityChangeKind kind)
    {
        var references = document.RefreshAffectedBlockReferenceBounds(entityIds);
        return new CadDocumentChangeSet(entityIds.Concat(references).Distinct()
            .Select(id => new CadEntityChange(id, kind)))
        {
            HasResolvedBlockReferenceChanges = true
        };
    }
}
