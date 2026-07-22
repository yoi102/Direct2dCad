using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Text;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Geometry;

internal static class CadGripDragGeometryFactory
{
    public static bool TryCreateBlockReferenceGripTransform(
        CadBlockDefinition definition,
        CadBlockReference reference,
        GripDragState drag,
        out CadPointD position,
        out double rotationRadians,
        out double scaleX,
        out double scaleY)
    {
        position = reference.Position;
        rotationRadians = reference.RotationRadians;
        scaleX = reference.ScaleX;
        scaleY = reference.ScaleY;

        if (drag.Handle.Type == CadHandleType.Rotation)
        {
            var original = drag.Handle.Position - reference.Position;
            var target = drag.DraggedGripPosition - reference.Position;
            if (original.LengthSquared <= double.Epsilon || target.LengthSquared <= double.Epsilon)
                return false;
            rotationRadians += Math.Atan2(target.Y, target.X) - Math.Atan2(original.Y, original.X);
            return true;
        }

        if (drag.Handle.Type != CadHandleType.BoundsCorner ||
            !CadBlockTransform.Create(definition, reference).TryInvert(out var inverse))
        {
            return false;
        }

        var localHandle = inverse.TransformPoint(drag.Handle.Position);
        var sourceX = localHandle.X - definition.BasePoint.X;
        var sourceY = localHandle.Y - definition.BasePoint.Y;
        var targetWorld = drag.DraggedGripPosition - reference.Position;
        var cos = Math.Cos(reference.RotationRadians);
        var sin = Math.Sin(reference.RotationRadians);
        var targetX = targetWorld.X * cos + targetWorld.Y * sin;
        var targetY = -targetWorld.X * sin + targetWorld.Y * cos;

        if (Math.Abs(sourceX) > 1e-9)
            scaleX = targetX / sourceX;
        if (Math.Abs(sourceY) > 1e-9)
            scaleY = targetY / sourceY;
        return Math.Abs(scaleX) > 1e-9 && Math.Abs(scaleY) > 1e-9 &&
               double.IsFinite(scaleX) && double.IsFinite(scaleY);
    }

    public static bool IsLineStartGrip(CadLine line, CadPointD gripPosition)
    {
        return line.Start.DistanceSquaredTo(gripPosition) <= line.End.DistanceSquaredTo(gripPosition);
    }

    public static bool TryCreateRectangleGripGeometry(
        CadRectangle rectangle,
        GripDragState drag,
        out CadRectD bounds)
    {
        return TryCreateBoundsGripGeometry(rectangle.Bounds, drag, out bounds);
    }

    public static bool TryCreateBoundsGripGeometry(
        CadRectD oldBounds,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = oldBounds;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || oldBounds.IsEmpty)
            return false;

        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - oldBounds.MinX) <= Math.Abs(drag.Handle.Position.X - oldBounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - oldBounds.MinY) <= Math.Abs(drag.Handle.Position.Y - oldBounds.MaxY);
        var oppositeX = dragLeft ? oldBounds.MaxX : oldBounds.MinX;
        var oppositeY = dragBottom ? oldBounds.MaxY : oldBounds.MinY;

        bounds = CadRectD.FromLTRB(oppositeX, oppositeY, target.X, target.Y);
        return IsValidRectangleBounds(bounds);
    }

    public static bool TryCreateImageGripGeometry(
        CadRectD oldBounds,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = oldBounds;

        if (oldBounds.IsEmpty)
            return false;

        return drag.Handle.Type switch
        {
            CadHandleType.BoundsCorner => TryCreateAspectBoundsGripGeometry(oldBounds, drag, out bounds),
            CadHandleType.BoundsSide => TryCreateSideBoundsGripGeometry(oldBounds, drag, out bounds),
            _ => false
        };
    }

    public static bool TryCreateImageGripGeometry(
        CadImage image,
        GripDragState drag,
        out CadRectD frameBounds,
        out double rotationRadians)
    {
        frameBounds = image.FrameBounds;
        rotationRadians = image.RotationRadians;

        if (drag.Handle.Type == CadHandleType.Rotation)
        {
            var target = drag.DraggedGripPosition;
            if (image.FrameBounds.Center.DistanceSquaredTo(target) <= double.Epsilon)
                return false;

            rotationRadians = Math.Atan2(
                target.Y - image.FrameBounds.Center.Y,
                target.X - image.FrameBounds.Center.X) - Math.PI * 0.5;
            return true;
        }

        var localHandle = drag.Handle with { Position = image.WorldToFrame(drag.Handle.Position) };
        var localTarget = image.WorldToFrame(drag.DraggedGripPosition);
        var localDrag = new GripDragState(
            localHandle,
            localHandle.Position,
            drag.PointIndex,
            drag.HiddenEntityIds)
        {
            CurrentPointerWorld = localTarget
        };

        if (!TryCreateImageGripGeometry(image.FrameBounds, localDrag, out var localBounds))
            return false;

        var worldCenter = image.FrameToWorld(localBounds.Center);
        frameBounds = CadRectD.FromCenter(worldCenter, localBounds.Width, localBounds.Height);
        return true;
    }

    private static bool TryCreateAspectBoundsGripGeometry(
        CadRectD oldBounds,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = oldBounds;

        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - oldBounds.MinX) <= Math.Abs(drag.Handle.Position.X - oldBounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - oldBounds.MinY) <= Math.Abs(drag.Handle.Position.Y - oldBounds.MaxY);
        var oppositeX = dragLeft ? oldBounds.MaxX : oldBounds.MinX;
        var oppositeY = dragBottom ? oldBounds.MaxY : oldBounds.MinY;
        var sourceSignX = dragLeft ? -1.0 : 1.0;
        var sourceSignY = dragBottom ? -1.0 : 1.0;
        var deltaX = target.X - oppositeX;
        var deltaY = target.Y - oppositeY;
        var signX = Math.Abs(deltaX) > double.Epsilon ? Math.Sign(deltaX) : sourceSignX;
        var signY = Math.Abs(deltaY) > double.Epsilon ? Math.Sign(deltaY) : sourceSignY;
        var scaleX = Math.Abs(deltaX) / oldBounds.Width;
        var scaleY = Math.Abs(deltaY) / oldBounds.Height;
        var scale = Math.Max(scaleX, scaleY);

        if (!IsFinitePositive(scale))
            return false;

        var width = oldBounds.Width * scale;
        var height = oldBounds.Height * scale;
        bounds = CadRectD.FromLTRB(
            oppositeX,
            oppositeY,
            oppositeX + signX * width,
            oppositeY + signY * height);
        return IsValidRectangleBounds(bounds);
    }

    private static bool TryCreateSideBoundsGripGeometry(
        CadRectD oldBounds,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = oldBounds;

        var target = drag.DraggedGripPosition;
        var handle = drag.Handle.Position;
        var horizontalOffset = Math.Min(
            Math.Abs(handle.X - oldBounds.MinX),
            Math.Abs(handle.X - oldBounds.MaxX));
        var verticalOffset = Math.Min(
            Math.Abs(handle.Y - oldBounds.MinY),
            Math.Abs(handle.Y - oldBounds.MaxY));

        if (horizontalOffset <= verticalOffset)
        {
            var dragLeft = Math.Abs(handle.X - oldBounds.MinX) <= Math.Abs(handle.X - oldBounds.MaxX);
            bounds = dragLeft
                ? CadRectD.FromLTRB(target.X, oldBounds.MinY, oldBounds.MaxX, oldBounds.MaxY)
                : CadRectD.FromLTRB(oldBounds.MinX, oldBounds.MinY, target.X, oldBounds.MaxY);
        }
        else
        {
            var dragBottom = Math.Abs(handle.Y - oldBounds.MinY) <= Math.Abs(handle.Y - oldBounds.MaxY);
            bounds = dragBottom
                ? CadRectD.FromLTRB(oldBounds.MinX, target.Y, oldBounds.MaxX, oldBounds.MaxY)
                : CadRectD.FromLTRB(oldBounds.MinX, oldBounds.MinY, oldBounds.MaxX, target.Y);
        }

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
