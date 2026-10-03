using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

internal static class CadCommandGeometryChanges
{
    // A child being created/deleted changes its block's geometry, not the lifetime of live references.
    public static CadDocumentChangeSet Resolve(CadDocument document, IReadOnlyList<CadEntityChange> entityChanges)
    {
        var references = document.RefreshAffectedBlockReferenceBounds(entityChanges.Select(change => change.EntityId).ToArray());
        var changes = new Dictionary<EntityId, CadEntityChangeKind>();
        foreach (var change in entityChanges)
            changes[change.EntityId] = changes.GetValueOrDefault(change.EntityId) | change.Kind;
        foreach (var id in references)
            changes[id] = changes.GetValueOrDefault(id) | CadEntityChangeKind.Geometry;
        return new CadDocumentChangeSet(changes.Select(change => new CadEntityChange(change.Key, change.Value)))
        {
            HasResolvedBlockReferenceChanges = true
        };
    }

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
