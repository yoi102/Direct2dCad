using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadClipboardInteractionService(
    CadEditor editor,
    CadPreviewStyleService styleService)
{
    public ClipboardSnapshot? CreateSelectionSnapshot()
    {
        if (editor.Selection.Count == 0)
            return null;

        var entityIds = new List<EntityId>();
        var bounds = CadRectD.Empty;
        foreach (var entityId in editor.Selection.EntityIds)
        {
            if (!editor.Document.TryGetEntity(entityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !CanDuplicate(entity))
            {
                continue;
            }

            entityIds.Add(entityId);
            bounds = bounds.Union(entity.Bounds);
        }

        return entityIds.Count == 0 || bounds.IsEmpty
            ? null
            : new ClipboardSnapshot(entityIds.ToArray(), bounds.Center, bounds);
    }

    public void AddPastePreview(
        List<CadTransientItem> items,
        ClipboardSnapshot? clipboard,
        bool isPastePreviewActive,
        CadPointD mouseWorld)
    {
        if (!isPastePreviewActive || clipboard is null)
            return;

        var delta = mouseWorld - clipboard.BasePoint;
        foreach (var entityId in clipboard.EntityIds)
        {
            if (editor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                !entity.IsErased)
            {
                items.Add(new CadTransientEntityReference(entityId, delta, styleService.CreateEntityPreviewStyle(entity)));
            }
        }

        items.Add(new CadTransientRectangle(
            clipboard.Bounds.Translate(delta),
            CadTransientStyle.PastePreview));
    }

    public IReadOnlyList<EntityId> CommitPaste(ClipboardSnapshot clipboard, CadPointD target)
    {
        var delta = target - clipboard.BasePoint;
        return editor.DuplicateEntities(clipboard.EntityIds, delta);
    }

    private static bool CanDuplicate(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadArc or CadRectangle or CadPolyline or CadSpline or CadText or CadShapeText;
    }
}
