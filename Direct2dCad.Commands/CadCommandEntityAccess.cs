using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

internal static class CadCommandEntityAccess
{
    public static void EnsureEditable(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadEntityAccessPolicy.EnsureEditable(document, document.GetEntity(entityId));
    }

    public static void EnsureEditable(CadDocument document, IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entityIds);

        foreach (var entityId in entityIds)
            CadEntityAccessPolicy.EnsureEditable(document, document.GetEntity(entityId));
    }
}
