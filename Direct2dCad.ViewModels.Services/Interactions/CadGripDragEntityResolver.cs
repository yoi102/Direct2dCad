using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal static class CadGripDragEntityResolver
{
    public static IReadOnlySet<EntityId> ResolveMoveEntityIds(
        CadEditor editor,
        CadGripHandle handle)
    {
        var result = new HashSet<EntityId>();
        if (handle.Type != CadHandleType.Center)
        {
            result.Add(handle.EntityId);
            return result;
        }

        var selectedEntityIds = editor.Selection.EntityIds;
        if (!selectedEntityIds.Contains(handle.EntityId))
        {
            result.Add(handle.EntityId);
            return result;
        }

        foreach (var entityId in selectedEntityIds)
        {
            if (IsMovableByCenterGrip(editor, entityId))
                result.Add(entityId);
        }

        if (result.Count == 0)
            result.Add(handle.EntityId);
        return result;
    }

    private static bool IsMovableByCenterGrip(CadEditor editor, EntityId entityId)
    {
        return editor.Document.TryGetEntity(entityId, out var entity) &&
               entity is not null &&
               CadEntityAccessPolicy.IsEditable(editor.Document, entity) &&
               CadHandleSceneBuilder.SupportsCenterGrip(entity);
    }
}
