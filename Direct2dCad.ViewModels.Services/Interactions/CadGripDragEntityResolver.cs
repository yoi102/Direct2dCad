using Direct2dCad.Db;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal static class CadGripDragEntityResolver
{
    public static IEnumerable<EntityId> ResolveMoveEntityIds(CadEditor editor, GripDragState drag)
    {
        if (drag.Handle.Type != CadHandleType.Center)
            return [drag.Handle.EntityId];

        var selectedEntityIds = editor.Selection.EntityIds;
        if (!selectedEntityIds.Contains(drag.Handle.EntityId))
            return [drag.Handle.EntityId];

        var movableSelectedEntityIds = selectedEntityIds
            .Where(entityId => IsMovableByCenterGrip(editor, entityId))
            .Distinct()
            .ToArray();

        return movableSelectedEntityIds.Length > 0
            ? movableSelectedEntityIds
            : [drag.Handle.EntityId];
    }

    private static bool IsMovableByCenterGrip(CadEditor editor, EntityId entityId)
    {
        return editor.Document.TryGetEntity(entityId, out var entity) &&
               entity is not null &&
               !entity.IsErased &&
               !entity.IsLocked &&
               CadHandleSceneBuilder.SupportsCenterGrip(entity);
    }
}
