using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Services.Text;
using static Direct2dCad.ViewModels.Services.Geometry.CadGripDragGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadGripDragCommitter(
    CadEditor editor,
    CadTextMeasurementService textMeasurementService)
{
    public void Commit(GripDragState drag)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            CommitMoveGripDrag(drag);
            return;
        }

        if (!editor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        switch (entity)
        {
            case CadLine line:
                CommitLineGripDrag(line, drag);
                break;

            case CadCircle circle:
                CommitCircleGripDrag(circle, drag);
                break;

            case CadEllipse ellipse:
                CommitEllipseGripDrag(ellipse, drag);
                break;

            case CadArc arc:
                CommitArcGripDrag(arc, drag);
                break;

            case CadRectangle rectangle:
                CommitRectangleGripDrag(rectangle, drag);
                break;

            case CadPolyline polyline:
                CommitPolylineGripDrag(polyline, drag);
                break;

            case CadSpline spline:
                CommitSplineGripDrag(spline, drag);
                break;

            case CadText text:
                CommitTextGripDrag(text, drag);
                break;

            case CadShapeText shapeText:
                CommitShapeTextGripDrag(shapeText, drag);
                break;
        }
    }

    private void CommitMoveGripDrag(GripDragState drag)
    {
        var entityIds = CadGripDragEntityResolver.ResolveMoveEntityIds(editor, drag).ToArray();
        if (entityIds.Length > 0)
            editor.MoveEntities(entityIds, drag.Delta);
    }

    private void CommitLineGripDrag(CadLine line, GripDragState drag)
    {
        var moveStart = IsLineStartGrip(line, drag.Handle.Position);
        editor.SetLineGeometry(
            line.Id,
            moveStart ? drag.DraggedGripPosition : line.Start,
            moveStart ? line.End : drag.DraggedGripPosition);
    }

    private void CommitCircleGripDrag(CadCircle circle, GripDragState drag)
    {
        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius > double.Epsilon)
            editor.SetCircleGeometry(circle.Id, circle.Center, radius);
    }

    private void CommitEllipseGripDrag(CadEllipse ellipse, GripDragState drag)
    {
        if (TryCreateEllipseGripGeometry(ellipse, drag, out var center, out var radiusX, out var radiusY))
            editor.SetEllipseGeometry(ellipse.Id, center, radiusX, radiusY);
    }

    private void CommitArcGripDrag(CadArc arc, GripDragState drag)
    {
        if (TryCreateArcGripGeometry(arc, drag, out var center, out var radius, out var startAngle, out var sweepAngle))
            editor.SetArcGeometry(arc.Id, center, radius, startAngle, sweepAngle);
    }

    private void CommitRectangleGripDrag(CadRectangle rectangle, GripDragState drag)
    {
        if (TryCreateRectangleGripGeometry(rectangle, drag, out var bounds))
            editor.SetRectangleGeometry(rectangle.Id, bounds);
    }

    private void CommitPolylineGripDrag(CadPolyline polyline, GripDragState drag)
    {
        if (TryCreatePolylineGripGeometry(polyline, drag, out var points, out var closed))
            editor.SetPolylineGeometry(polyline.Id, points, closed);
    }

    private void CommitSplineGripDrag(CadSpline spline, GripDragState drag)
    {
        if (TryCreateSplineGripGeometry(spline, drag, out var fitPoints, out var closed))
            editor.SetSplineGeometry(spline.Id, fitPoints, closed);
    }

    private void CommitTextGripDrag(CadText text, GripDragState drag)
    {
        var grid = editor.Document.ViewSettings.Grid;
        if (TryCreateTextGripGeometry(
            text,
            drag,
            grid.GetSnapSpacingX(),
            grid.GetSnapSpacingY(),
            textMeasurementService,
            out var position,
            out var height))
        {
            editor.SetTextGeometry(text.Id, position, height, text.RotationRadians);
        }
    }

    private void CommitShapeTextGripDrag(CadShapeText text, GripDragState drag)
    {
        if (TryCreateShapeTextGripGeometry(text, drag, out var position, out var height))
        {
            editor.SetShapeTextGeometry(
                text.Id,
                position,
                height,
                text.RotationRadians,
                text.WidthFactor,
                text.CharacterSpacingFactor,
                text.ObliqueAngleRadians);
        }
    }
}
