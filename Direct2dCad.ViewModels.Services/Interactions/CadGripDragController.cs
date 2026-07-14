using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using static Direct2dCad.ViewModels.Services.Geometry.CadGripDragGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadGripDragController(CadHandleHitTester hitTester)
{
    public GripDragState? ActiveDrag { get; private set; }

    public bool IsActive => ActiveDrag is not null;

    public bool TryBegin(
        CadEditor editor,
        CadHandleScene handleScene,
        Func<CadPointD, CadPointD> worldToScreen,
        Func<CadPointD, CadPointD> screenToWorld,
        CadPointD screen)
    {
        if (!hitTester.TryHitGrip(handleScene, worldToScreen, screen, out var grip))
            return false;

        if (!editor.Document.TryGetEntity(grip.EntityId, out var entity) ||
            entity is null ||
            !CadEntityAccessPolicy.IsEditable(editor.Document, entity))
        {
            return false;
        }

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
            !CadEntityAccessPolicy.IsEditable(editor.Document, entity))
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

    public IReadOnlyList<CadHandleItem>? CreateActiveHandleItems(
        CadEditor editor,
        CadHandleSceneBuildOptions options,
        double interactionZoom)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(options);

        if (ActiveDrag is not { } drag)
            return null;

        if (editor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) &&
            entity is CadImage image &&
            !image.IsErased &&
            TryCreateImageGripGeometry(image, drag, out var frameBounds, out var rotationRadians))
        {
            var effectiveOptions = options with
            {
                RotationHandleOffset = 28.0 / Math.Max(interactionZoom, double.Epsilon)
            };
            return new CadHandleSceneBuilder().BuildImageGripHandles(
                image.Id,
                frameBounds,
                rotationRadians,
                effectiveOptions);
        }

        if (entity is CadBlockReference reference && !reference.IsErased)
        {
            var blockPosition = reference.Position;
            var blockRotationRadians = reference.RotationRadians;
            var blockScaleX = reference.ScaleX;
            var blockScaleY = reference.ScaleY;
            var hasTransform = drag.Handle.Type == CadHandleType.Center;
            if (hasTransform)
            {
                blockPosition += drag.Delta;
            }
            else
            {
                var definition = editor.Document.GetBlock(reference.DefinitionBlockId);
                hasTransform = TryCreateBlockReferenceGripTransform(
                    definition,
                    reference,
                    drag,
                    out blockPosition,
                    out blockRotationRadians,
                    out blockScaleX,
                    out blockScaleY);
            }

            if (hasTransform)
            {
                var effectiveOptions = options with
                {
                    RotationHandleOffset = 28.0 / Math.Max(interactionZoom, double.Epsilon)
                };
                return new CadHandleSceneBuilder().BuildBlockReferenceGripHandles(
                    editor.Document,
                    reference.Id,
                    reference.DefinitionBlockId,
                    blockPosition,
                    blockRotationRadians,
                    blockScaleX,
                    blockScaleY,
                    effectiveOptions);
            }
        }

        return [drag.Handle with { Position = drag.DraggedGripPosition }];
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
