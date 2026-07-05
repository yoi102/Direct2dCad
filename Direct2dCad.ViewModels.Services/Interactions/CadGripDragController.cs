using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using static Direct2dCad.ViewModels.Geometry.CadGripDragGeometryFactory;

namespace Direct2dCad.ViewModels.Interactions;

internal sealed class CadGripDragController(CadHandleHitTester hitTester)
{
    public GripDragState? ActiveDrag { get; private set; }

    public bool IsActive => ActiveDrag is not null;

    public bool TryBegin(
        CadEditor editor,
        CadHandleScene handleScene,
        Func<CadPointD, CadPointD> screenToWorld,
        CadPointD screen)
    {
        if (!hitTester.TryHitGrip(handleScene, editor.Viewport.WorldToScreen, screen, out var grip))
            return false;

        ActiveDrag = new GripDragState(
            grip,
            screenToWorld(screen),
            ResolveGripPointIndex(editor, grip));
        return true;
    }

    public void UpdatePointer(Func<CadPointD, CadPointD> screenToWorld, CadPointD screen)
    {
        if (ActiveDrag is { } drag)
            drag.CurrentPointerWorld = screenToWorld(screen);
    }

    public bool Commit(
        CadEditor editor,
        CadGripDragCommitter committer,
        Func<CadPointD, CadPointD> screenToWorld,
        CadPointD screen)
    {
        if (ActiveDrag is not { } drag)
            return false;

        ActiveDrag = null;
        drag.CurrentPointerWorld = screenToWorld(screen);

        if (drag.Delta.Length <= 1e-9)
            return false;

        if (!editor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return false;
        }

        committer.Commit(drag);
        return true;
    }

    public IEnumerable<EntityId> ResolveHiddenEntityIds(CadEditor editor)
    {
        return ActiveDrag is null
            ? []
            : CadGripDragEntityResolver.ResolveMoveEntityIds(editor, ActiveDrag);
    }

    public CadGripHandle? CreateActiveGripHandle()
    {
        return ActiveDrag is { } drag
            ? drag.Handle with { Position = drag.DraggedGripPosition }
            : null;
    }

    public void Clear()
    {
        ActiveDrag = null;
    }

    private static int ResolveGripPointIndex(CadEditor editor, CadGripHandle grip)
    {
        if (grip.Type != CadHandleType.Vertex ||
            !editor.Document.TryGetEntity(grip.EntityId, out var entity) ||
            entity is null)
        {
            return -1;
        }

        return entity switch
        {
            CadPolyline polyline => FindNearestPointIndex(polyline.Points, grip.Position),
            CadSpline spline => FindNearestPointIndex(spline.FitPoints, grip.Position),
            _ => -1
        };
    }
}
