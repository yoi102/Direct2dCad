using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Text;
using static Direct2dCad.ViewModels.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Geometry;

internal static class CadGripDragGeometryFactory
{
    public static bool IsLineStartGrip(CadLine line, CadPointD gripPosition)
    {
        return line.Start.DistanceSquaredTo(gripPosition) <= line.End.DistanceSquaredTo(gripPosition);
    }

    public static bool TryCreateRectangleGripGeometry(
        CadRectangle rectangle,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = rectangle.Bounds;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || rectangle.Bounds.IsEmpty)
            return false;

        var oldBounds = rectangle.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - oldBounds.MinX) <= Math.Abs(drag.Handle.Position.X - oldBounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - oldBounds.MinY) <= Math.Abs(drag.Handle.Position.Y - oldBounds.MaxY);
        var oppositeX = dragLeft ? oldBounds.MaxX : oldBounds.MinX;
        var oppositeY = dragBottom ? oldBounds.MaxY : oldBounds.MinY;

        bounds = CadRectD.FromLTRB(oppositeX, oppositeY, target.X, target.Y);
        return IsValidRectangleBounds(bounds);
    }

    public static bool TryCreateEllipseGripGeometry(
        CadEllipse ellipse,
        GripDragState drag,
        out CadPointD center,
        out double radiusX,
        out double radiusY)
    {
        center = ellipse.Center;
        radiusX = ellipse.RadiusX;
        radiusY = ellipse.RadiusY;

        if (drag.Handle.Type != CadHandleType.Radius)
            return false;

        var isHorizontalRadiusGrip =
            Math.Abs(drag.Handle.Position.X - ellipse.Center.X) >=
            Math.Abs(drag.Handle.Position.Y - ellipse.Center.Y);

        if (isHorizontalRadiusGrip)
            radiusX = Math.Abs(drag.DraggedGripPosition.X - ellipse.Center.X);
        else
            radiusY = Math.Abs(drag.DraggedGripPosition.Y - ellipse.Center.Y);

        return IsValidEllipseGeometry(radiusX, radiusY);
    }

    public static bool TryCreatePolylineGripGeometry(
        CadPolyline polyline,
        GripDragState drag,
        out CadPointD[] points,
        out bool closed)
    {
        points = polyline.Points.ToArray();
        closed = polyline.Closed;

        if (drag.Handle.Type != CadHandleType.Vertex || points.Length < 2)
            return false;

        var vertexIndex = drag.PointIndex;
        if (vertexIndex < 0)
            return false;
        if (vertexIndex >= points.Length)
            return false;

        points[vertexIndex] = drag.DraggedGripPosition;
        return !closed || points.Length >= 3;
    }

    public static bool TryCreateSplineGripGeometry(
        CadSpline spline,
        GripDragState drag,
        out CadPointD[] fitPoints,
        out bool closed)
    {
        fitPoints = spline.FitPoints.ToArray();
        closed = spline.Closed;

        if (drag.Handle.Type != CadHandleType.Vertex || fitPoints.Length < 2)
            return false;

        var fitPointIndex = drag.PointIndex;
        if (fitPointIndex < 0)
            return false;
        if (fitPointIndex >= fitPoints.Length)
            return false;

        fitPoints[fitPointIndex] = drag.DraggedGripPosition;
        return !closed || fitPoints.Length >= 3;
    }

    public static bool TryCreateArcGripGeometry(
        CadArc arc,
        GripDragState drag,
        out CadPointD center,
        out double radius,
        out double startAngleRadians,
        out double sweepAngleRadians)
    {
        center = arc.Center;
        radius = arc.Radius;
        startAngleRadians = arc.StartAngleRadians;
        sweepAngleRadians = arc.SweepAngleRadians;

        if (drag.Handle.Type == CadHandleType.Radius)
        {
            radius = center.DistanceTo(drag.DraggedGripPosition);
            return radius > double.Epsilon;
        }

        if (drag.Handle.Type != CadHandleType.Vertex)
            return false;

        var targetRadius = center.DistanceTo(drag.DraggedGripPosition);
        if (targetRadius <= double.Epsilon)
            return false;

        radius = targetRadius;
        var targetAngle = AngleFrom(center, drag.DraggedGripPosition);
        if (IsArcStartGrip(arc, drag.Handle.Position))
        {
            startAngleRadians = targetAngle;
            sweepAngleRadians = ResolveSweepAngle(
                startAngleRadians,
                arc.EndAngleRadians,
                arc.SweepAngleRadians >= 0);
        }
        else
        {
            sweepAngleRadians = ResolveSweepAngle(
                startAngleRadians,
                targetAngle,
                arc.SweepAngleRadians >= 0);
        }

        return IsValidArcGeometry(radius, sweepAngleRadians);
    }

    public static int FindNearestPointIndex(IReadOnlyList<CadPointD> points, CadPointD target)
    {
        var index = -1;
        var bestDistance = double.PositiveInfinity;

        for (var i = 0; i < points.Count; i++)
        {
            var distance = points[i].DistanceSquaredTo(target);
            if (distance >= bestDistance)
                continue;

            index = i;
            bestDistance = distance;
        }

        return index;
    }

    public static bool TryCreateTextGripGeometry(
        CadText text,
        GripDragState drag,
        double snapSpacingX,
        double snapSpacingY,
        CadTextMeasurementService textMeasurementService,
        out CadPointD position,
        out double height)
    {
        position = text.Position;
        height = text.Height;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || text.Bounds.IsEmpty)
            return false;

        var bounds = text.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - bounds.MinX) <= Math.Abs(drag.Handle.Position.X - bounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - bounds.MinY) <= Math.Abs(drag.Handle.Position.Y - bounds.MaxY);
        var oppositeX = dragLeft ? bounds.MaxX : bounds.MinX;
        var oppositeY = dragBottom ? bounds.MaxY : bounds.MinY;
        var widthFactor = CadTextMeasurementService.GetCachedTextWidthFactor(text);
        var marginFactor = text.IsInverted ? text.InvertedMarginFactor : 0;
        var heightScale = 1.0 + marginFactor * 2.0;
        var widthScale = widthFactor + marginFactor * 2.0;
        var desiredHeight = Math.Abs(target.Y - oppositeY);
        var desiredWidth = Math.Abs(target.X - oppositeX);

        height = textMeasurementService.SnapTextHeightUp(
            text.Text,
            Math.Max(desiredHeight / heightScale, desiredWidth / widthScale),
            snapSpacingX,
            snapSpacingY,
            text.TextStyleId);
        var width = textMeasurementService.MeasureTextWidth(text.Text, height, text.TextStyleId);
        var margin = height * marginFactor;
        var outerWidth = width + margin * 2.0;
        var outerHeight = height + margin * 2.0;
        position = new CadPointD(
            (dragLeft ? oppositeX - outerWidth : oppositeX) + margin,
            (dragBottom ? oppositeY - outerHeight : oppositeY) + margin);
        return true;
    }

    public static bool TryCreateShapeTextGripGeometry(
        CadShapeText text,
        GripDragState drag,
        out CadPointD position,
        out double height)
    {
        position = text.Position;
        height = text.Height;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || text.Bounds.IsEmpty)
            return false;

        var bounds = text.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - bounds.MinX) <= Math.Abs(drag.Handle.Position.X - bounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - bounds.MinY) <= Math.Abs(drag.Handle.Position.Y - bounds.MaxY);
        var oppositeX = dragLeft ? bounds.MaxX : bounds.MinX;
        var oppositeY = dragBottom ? bounds.MaxY : bounds.MinY;
        var widthFactor = CadTextMeasurementService.GetCachedShapeTextWidthFactor(text);
        var marginFactor = text.IsInverted ? text.InvertedMarginFactor : 0;
        var heightScale = 1.0 + marginFactor * 2.0;
        var widthScale = widthFactor + marginFactor * 2.0;
        var desiredHeight = Math.Abs(target.Y - oppositeY);
        var desiredWidth = Math.Abs(target.X - oppositeX);

        height = Math.Max(desiredHeight / heightScale, desiredWidth / widthScale);
        if (!IsFinitePositive(height))
            return false;

        var width = Math.Max(text.TextBounds.Width / Math.Max(text.Height, double.Epsilon) * height, height * text.WidthFactor);
        var margin = height * marginFactor;
        var outerWidth = width + margin * 2.0;
        var outerHeight = height + margin * 2.0;
        position = new CadPointD(
            (dragLeft ? oppositeX - outerWidth : oppositeX) + margin,
            (dragBottom ? oppositeY - outerHeight : oppositeY) + margin);
        return true;
    }

    private static bool IsArcStartGrip(CadArc arc, CadPointD gripPosition)
    {
        return arc.StartPoint.DistanceSquaredTo(gripPosition) <= arc.EndPoint.DistanceSquaredTo(gripPosition);
    }

    private static bool IsValidRectangleBounds(CadRectD bounds)
    {
        return !bounds.IsEmpty &&
               bounds.Width > double.Epsilon &&
               bounds.Height > double.Epsilon;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
