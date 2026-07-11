using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting;

/// <summary>
/// CAD 几何 HitTest 服务。
/// 不放到 CadEntity 上，因为不是所有 Entity 都有边缘/填充，
/// 而且 CadBlockReference 的 HitTest 需要访问 CadDocument。
/// </summary>
public static class CadEntityHitTester
{
    private const double Epsilon = 1e-12;
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>
    /// 点击边缘。
    /// tolerance 使用世界坐标单位。
    /// </summary>
    public static bool HitTestEdge(
        CadDocument document,
        CadEntity entity,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        return HitTestEdge(
            document,
            entity,
            point,
            tolerance,
            CadHitTestOptions.Default,
            out result);
    }

    public static bool HitTestEdge(
        CadDocument document,
        CadEntity entity,
        CadPointD point,
        double tolerance,
        CadHitTestOptions options,
        out CadHitTestResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);
        GuardTolerance(tolerance);

        return HitTestEdgeCore(
            document,
            entity,
            point,
            tolerance,
            options,
            new HashSet<BlockId>(),
            out result);
    }

    /// <summary>
    /// 点击填充区域。
    /// 只有拥有 FillStyleId 的闭合实体才会命中填充。
    /// </summary>
    public static bool HitTestFill(
        CadDocument document,
        CadEntity entity,
        CadPointD point,
        out CadHitTestResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entity);

        return HitTestFillCore(
            document,
            entity,
            point,
            [],
            out result);
    }

    private static bool HitTestEdgeCore(
        CadDocument document,
        CadEntity entity,
        CadPointD point,
        double tolerance,
        CadHitTestOptions options,
        HashSet<BlockId> visitedBlocks,
        out CadHitTestResult result)
    {
        result = default;

        if (entity.IsErased)
            return false;

        var edgeTolerance = tolerance + CadHitTestStyleResolver.ResolveStrokeHitPadding(
            document,
            entity,
            options);

        switch (entity)
        {
            case CadLine line:
                return HitLineEdge(line, point, edgeTolerance, out result);

            case CadArc arc:
                return HitArcEdge(arc, point, edgeTolerance, out result);

            case CadCircle circle:
                return HitCircleEdge(circle, point, edgeTolerance, out result);

            case CadEllipse ellipse:
                return HitEllipseEdge(ellipse, point, edgeTolerance, out result);

            case CadEllipseArc ellipseArc:
                return HitEllipseArcEdge(ellipseArc, point, edgeTolerance, out result);

            case CadRectangle rectangle:
                return HitRectangleEdge(rectangle, point, edgeTolerance, out result);

            case CadPolyline polyline:
                return HitPolylineEdge(polyline, point, edgeTolerance, out result);

            case CadSpline spline:
                return HitSplineEdge(spline, point, edgeTolerance, out result);

            case CadShapeText shapeText:
                return HitShapeTextEdge(shapeText, point, edgeTolerance, out result);

            case CadImage image:
                return HitImageEdge(image, point, edgeTolerance, out result);

            case CadOleObject oleObject:
                return HitRectEdge(oleObject.Id, oleObject.Bounds, point, edgeTolerance, out result);

            case CadBlockReference blockReference:
                return HitBlockReferenceEdge(
                    document,
                    blockReference,
                    point,
                    tolerance,
                    options,
                    visitedBlocks,
                    out result);

            default:
                return false;
        }
    }

    private static bool HitTestFillCore(
        CadDocument document,
        CadEntity entity,
        CadPointD point,
        HashSet<BlockId> visitedBlocks,
        out CadHitTestResult result)
    {
        result = default;

        if (entity.IsErased)
            return false;

        switch (entity)
        {
            case CadCircle circle:
                return HitCircleFill(circle, point, out result);

            case CadEllipse ellipse:
                return HitEllipseFill(ellipse, point, out result);

            case CadRectangle rectangle:
                return HitRectangleFill(rectangle, point, out result);

            case CadPolyline polyline:
                return HitPolylineFill(polyline, point, out result);

            case CadSpline spline:
                return HitSplineFill(spline, point, out result);

            case CadText text:
                return HitTextFill(text, point, out result);

            case CadImage image:
                return HitImageFill(image, point, out result);

            case CadOleObject oleObject:
                return HitOleObjectFill(oleObject, point, out result);

            case CadBlockReference blockReference:
                return HitBlockReferenceFill(
                    document,
                    blockReference,
                    point,
                    visitedBlocks,
                    out result);

            default:
                return false;
        }
    }

    private static bool HitLineEdge(
        CadLine line,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        var distance = DistancePointToSegment(point, line.Start, line.End);
        if (distance > tolerance)
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [line.Id],
            point,
            distance);

        return true;
    }

    private static bool HitCircleEdge(
        CadCircle circle,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        var distanceToCenter = circle.Center.DistanceTo(point);
        var distance = Math.Abs(distanceToCenter - circle.Radius);

        if (distance > tolerance)
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [circle.Id],
            point,
            distance);

        return true;
    }

    private static bool HitCircleFill(
        CadCircle circle,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (circle.FillStyleId is null)
        {
            result = default;
            return false;
        }

        var distanceSquared = circle.Center.DistanceSquaredTo(point);
        if (distanceSquared > circle.Radius * circle.Radius)
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [circle.Id],
            point);

        return true;
    }

    private static bool HitEllipseEdge(
        CadEllipse ellipse,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        var dx = point.X - ellipse.Center.X;
        var dy = point.Y - ellipse.Center.Y;
        var angle = Math.Atan2(dy / ellipse.RadiusY, dx / ellipse.RadiusX);
        var edgePoint = new CadPointD(
            ellipse.Center.X + Math.Cos(angle) * ellipse.RadiusX,
            ellipse.Center.Y + Math.Sin(angle) * ellipse.RadiusY);
        var distance = point.DistanceTo(edgePoint);

        if (distance > tolerance)
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [ellipse.Id],
            point,
            distance);

        return true;
    }

    private static bool HitEllipseFill(
        CadEllipse ellipse,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (ellipse.FillStyleId is null)
        {
            result = default;
            return false;
        }

        if (!IsPointInsideEllipse(point, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [ellipse.Id],
            point);

        return true;
    }

    private static bool HitEllipseArcEdge(
        CadEllipseArc ellipseArc,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        var dx = point.X - ellipseArc.Center.X;
        var dy = point.Y - ellipseArc.Center.Y;
        var angle = Math.Atan2(dy / ellipseArc.RadiusY, dx / ellipseArc.RadiusX);
        if (!ContainsArcAngle(ellipseArc.StartAngleRadians, ellipseArc.SweepAngleRadians, angle))
        {
            result = default;
            return false;
        }

        var edgePoint = ellipseArc.GetPointAtAngle(angle);
        var distance = point.DistanceTo(edgePoint);
        if (distance > tolerance)
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [ellipseArc.Id],
            point,
            distance);

        return true;
    }

    private static bool HitRectangleFill(
        CadRectangle rectangle,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (rectangle.FillStyleId is null || !IsPointInsideRectangle(rectangle, point))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [rectangle.Id],
            point);

        return true;
    }

    private static bool HitRectangleEdge(
        CadRectangle rectangle,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        if (!rectangle.HasRoundedCorners)
            return HitRectEdge(rectangle.Id, rectangle.Bounds, point, tolerance, out result);

        result = default;
        var bounds = rectangle.Bounds;
        if (bounds.IsEmpty)
            return false;

        var radiusX = ClampCornerRadius(rectangle.CornerRadiusX, bounds.Width);
        var radiusY = ClampCornerRadius(rectangle.CornerRadiusY, bounds.Height);
        if (radiusX <= Epsilon || radiusY <= Epsilon)
            return HitRectEdge(rectangle.Id, bounds, point, tolerance, out result);

        var bestDistance = double.PositiveInfinity;
        bestDistance = Math.Min(bestDistance, DistancePointToSegment(
            point,
            new CadPointD(bounds.MinX + radiusX, bounds.MinY),
            new CadPointD(bounds.MaxX - radiusX, bounds.MinY)));
        bestDistance = Math.Min(bestDistance, DistancePointToSegment(
            point,
            new CadPointD(bounds.MaxX, bounds.MinY + radiusY),
            new CadPointD(bounds.MaxX, bounds.MaxY - radiusY)));
        bestDistance = Math.Min(bestDistance, DistancePointToSegment(
            point,
            new CadPointD(bounds.MaxX - radiusX, bounds.MaxY),
            new CadPointD(bounds.MinX + radiusX, bounds.MaxY)));
        bestDistance = Math.Min(bestDistance, DistancePointToSegment(
            point,
            new CadPointD(bounds.MinX, bounds.MaxY - radiusY),
            new CadPointD(bounds.MinX, bounds.MinY + radiusY)));

        bestDistance = Math.Min(bestDistance, DistanceToCornerEllipseEdge(
            point,
            new CadPointD(bounds.MinX + radiusX, bounds.MinY + radiusY),
            radiusX,
            radiusY,
            dxSign: -1,
            dySign: -1));
        bestDistance = Math.Min(bestDistance, DistanceToCornerEllipseEdge(
            point,
            new CadPointD(bounds.MaxX - radiusX, bounds.MinY + radiusY),
            radiusX,
            radiusY,
            dxSign: 1,
            dySign: -1));
        bestDistance = Math.Min(bestDistance, DistanceToCornerEllipseEdge(
            point,
            new CadPointD(bounds.MaxX - radiusX, bounds.MaxY - radiusY),
            radiusX,
            radiusY,
            dxSign: 1,
            dySign: 1));
        bestDistance = Math.Min(bestDistance, DistanceToCornerEllipseEdge(
            point,
            new CadPointD(bounds.MinX + radiusX, bounds.MaxY - radiusY),
            radiusX,
            radiusY,
            dxSign: -1,
            dySign: 1));

        if (bestDistance > tolerance)
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [rectangle.Id],
            point,
            bestDistance);

        return true;
    }

    private static bool HitTextFill(
        CadText text,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (!text.Bounds.Contains(point))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [text.Id],
            point);

        return true;
    }

    private static bool HitImageFill(
        CadImage image,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (!image.FrameBounds.Contains(image.WorldToFrame(point)))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [image.Id],
            point);

        return true;
    }

    private static bool HitImageEdge(
        CadImage image,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        if (!HitRectEdge(
                image.Id,
                image.FrameBounds,
                image.WorldToFrame(point),
                tolerance,
                out var localResult))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [image.Id],
            point,
            localResult.Distance);
        return true;
    }

    private static bool HitOleObjectFill(
        CadOleObject oleObject,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (!oleObject.Bounds.Contains(point))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [oleObject.Id],
            point);

        return true;
    }

    private static bool HitArcEdge(
        CadArc arc,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        var distanceToCenter = arc.Center.DistanceTo(point);
        var radialDistance = Math.Abs(distanceToCenter - arc.Radius);

        if (radialDistance > tolerance)
        {
            result = default;
            return false;
        }

        var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X);
        if (!ContainsArcAngle(arc.StartAngleRadians, arc.SweepAngleRadians, angle))
        {
            result = default;
            return false;
        }

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [arc.Id],
            point,
            radialDistance);

        return true;
    }

    private static bool HitPolylineEdge(
        CadPolyline polyline,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        result = default;

        var points = polyline.Points;
        if (points.Count < 2)
            return false;

        var bestDistance = double.PositiveInfinity;

        for (var i = 1; i < points.Count; i++)
        {
            var distance = DistancePointToSegment(point, points[i - 1], points[i]);
            if (distance < bestDistance)
                bestDistance = distance;
        }

        if (polyline.IsClosed)
        {
            var distance = DistancePointToSegment(point, points[^1], points[0]);
            if (distance < bestDistance)
                bestDistance = distance;
        }

        if (bestDistance > tolerance)
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [polyline.Id],
            point,
            bestDistance);

        return true;
    }

    private static bool HitPolylineFill(
        CadPolyline polyline,
        CadPointD point,
        out CadHitTestResult result)
    {
        result = default;

        if (!polyline.IsClosed || polyline.FillStyleId is null)
            return false;

        if (!PointInPolygon(point, polyline.Points))
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [polyline.Id],
            point);

        return true;
    }

    private static bool HitSplineEdge(
        CadSpline spline,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        result = default;

        var flattened = spline.EnumerateFlattenedPoints(24).ToArray();
        if (flattened.Length < 2)
            return false;

        var bestDistance = double.PositiveInfinity;
        for (var i = 1; i < flattened.Length; i++)
        {
            var distance = DistancePointToSegment(point, flattened[i - 1], flattened[i]);
            if (distance < bestDistance)
                bestDistance = distance;
        }

        if (bestDistance > tolerance)
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [spline.Id],
            point,
            bestDistance);

        return true;
    }

    private static bool HitSplineFill(
        CadSpline spline,
        CadPointD point,
        out CadHitTestResult result)
    {
        result = default;

        if (!spline.IsClosed || spline.FillStyleId is null)
            return false;

        var flattened = spline.EnumerateFlattenedPoints(24).ToArray();
        if (flattened.Length < 3 || !PointInPolygon(point, flattened))
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Fill,
            [spline.Id],
            point);

        return true;
    }

    private static bool HitRectEdge(
        EntityId entityId,
        CadRectD rect,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        result = default;

        if (rect.IsEmpty)
            return false;

        var p1 = new CadPointD(rect.MinX, rect.MinY);
        var p2 = new CadPointD(rect.MaxX, rect.MinY);
        var p3 = new CadPointD(rect.MaxX, rect.MaxY);
        var p4 = new CadPointD(rect.MinX, rect.MaxY);

        var distance = Math.Min(
            Math.Min(DistancePointToSegment(point, p1, p2), DistancePointToSegment(point, p2, p3)),
            Math.Min(DistancePointToSegment(point, p3, p4), DistancePointToSegment(point, p4, p1)));

        if (distance > tolerance)
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            new[] { entityId },
            point,
            distance);

        return true;
    }

    private static bool HitShapeTextEdge(
        CadShapeText text,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        result = default;
        var bestDistance = double.PositiveInfinity;

        foreach (var segment in text.CreateStrokeSegments())
        {
            var distance = DistancePointToSegment(point, segment.Start, segment.End);
            if (distance < bestDistance)
                bestDistance = distance;
        }

        if (bestDistance > tolerance)
            return false;

        result = new CadHitTestResult(
            CadHitTestKind.Edge,
            [text.Id],
            point,
            bestDistance);

        return true;
    }

    private static bool HitBlockReferenceEdge(
        CadDocument document,
        CadBlockReference blockReference,
        CadPointD worldPoint,
        double worldTolerance,
        CadHitTestOptions options,
        HashSet<BlockId> visitedBlocks,
        out CadHitTestResult result)
    {
        result = default;

        if (!visitedBlocks.Add(blockReference.DefinitionBlockId))
            return false;

        try
        {
            var localPoint = TransformWorldToBlockLocal(
                document,
                blockReference,
                worldPoint);

            var localTolerance = TransformWorldToleranceToBlockLocal(
                blockReference,
                worldTolerance);

            foreach (var child in document.GetEntitiesInBlock(blockReference.DefinitionBlockId).Reverse())
            {
                if (HitTestEdgeCore(
                        document,
                        child,
                        localPoint,
                        localTolerance,
                        options,
                        visitedBlocks,
                        out var childResult))
                {
                    result = childResult.Prepend(blockReference.Id);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            visitedBlocks.Remove(blockReference.DefinitionBlockId);
        }
    }

    private static bool HitBlockReferenceFill(
        CadDocument document,
        CadBlockReference blockReference,
        CadPointD worldPoint,
        HashSet<BlockId> visitedBlocks,
        out CadHitTestResult result)
    {
        result = default;

        if (!visitedBlocks.Add(blockReference.DefinitionBlockId))
            return false;

        try
        {
            var localPoint = TransformWorldToBlockLocal(
                document,
                blockReference,
                worldPoint);

            foreach (var child in document.GetEntitiesInBlock(blockReference.DefinitionBlockId).Reverse())
            {
                if (HitTestFillCore(
                        document,
                        child,
                        localPoint,
                        visitedBlocks,
                        out var childResult))
                {
                    result = childResult.Prepend(blockReference.Id);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            visitedBlocks.Remove(blockReference.DefinitionBlockId);
        }
    }

    private static CadPointD TransformWorldToBlockLocal(
        CadDocument document,
        CadBlockReference blockReference,
        CadPointD worldPoint)
    {
        var definition = document.GetBlock(blockReference.DefinitionBlockId);

        var dx = worldPoint.X - blockReference.Position.X;
        var dy = worldPoint.Y - blockReference.Position.Y;

        var cos = Math.Cos(-blockReference.RotationRadians);
        var sin = Math.Sin(-blockReference.RotationRadians);

        var rotatedX = dx * cos - dy * sin;
        var rotatedY = dx * sin + dy * cos;

        return new CadPointD(
            rotatedX / blockReference.ScaleX + definition.BasePoint.X,
            rotatedY / blockReference.ScaleY + definition.BasePoint.Y);
    }

    private static double TransformWorldToleranceToBlockLocal(
        CadBlockReference blockReference,
        double worldTolerance)
    {
        // 非等比缩放下，精确距离需要椭圆度量。
        // 这里用较小缩放做保守换算，避免漏选。
        var scale = Math.Min(Math.Abs(blockReference.ScaleX), Math.Abs(blockReference.ScaleY));

        return scale <= Epsilon
            ? worldTolerance
            : worldTolerance / scale;
    }

    private static double DistancePointToSegment(
        CadPointD point,
        CadPointD start,
        CadPointD end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Epsilon)
            return point.DistanceTo(start);

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var projection = new CadPointD(
            start.X + t * dx,
            start.Y + t * dy);

        return point.DistanceTo(projection);
    }

    private static bool IsPointInsideEllipse(CadPointD point, CadPointD center, double radiusX, double radiusY)
    {
        var normalizedX = (point.X - center.X) / radiusX;
        var normalizedY = (point.Y - center.Y) / radiusY;
        return normalizedX * normalizedX + normalizedY * normalizedY <= 1.0 + Epsilon;
    }

    private static bool IsPointInsideRectangle(CadRectangle rectangle, CadPointD point)
    {
        var bounds = rectangle.Bounds;
        if (!bounds.Contains(point))
            return false;

        if (!rectangle.HasRoundedCorners)
            return true;

        var radiusX = ClampCornerRadius(rectangle.CornerRadiusX, bounds.Width);
        var radiusY = ClampCornerRadius(rectangle.CornerRadiusY, bounds.Height);
        if (radiusX <= Epsilon || radiusY <= Epsilon)
            return true;

        if (point.X >= bounds.MinX + radiusX && point.X <= bounds.MaxX - radiusX)
            return true;

        if (point.Y >= bounds.MinY + radiusY && point.Y <= bounds.MaxY - radiusY)
            return true;

        var centerX = point.X < bounds.MinX + radiusX
            ? bounds.MinX + radiusX
            : bounds.MaxX - radiusX;
        var centerY = point.Y < bounds.MinY + radiusY
            ? bounds.MinY + radiusY
            : bounds.MaxY - radiusY;

        return IsPointInsideEllipse(point, new CadPointD(centerX, centerY), radiusX, radiusY);
    }

    private static double DistanceToCornerEllipseEdge(
        CadPointD point,
        CadPointD center,
        double radiusX,
        double radiusY,
        int dxSign,
        int dySign)
    {
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;

        if ((dxSign < 0 && dx > Epsilon) ||
            (dxSign > 0 && dx < -Epsilon) ||
            (dySign < 0 && dy > Epsilon) ||
            (dySign > 0 && dy < -Epsilon))
        {
            return double.PositiveInfinity;
        }

        var angle = Math.Atan2(dy / radiusY, dx / radiusX);
        var edgePoint = new CadPointD(
            center.X + Math.Cos(angle) * radiusX,
            center.Y + Math.Sin(angle) * radiusY);
        return point.DistanceTo(edgePoint);
    }

    private static double ClampCornerRadius(double radius, double size)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? 0
            : Math.Min(radius, size * 0.5);
    }

    private static bool PointInPolygon(CadPointD point, IReadOnlyList<CadPointD> polygon)
    {
        var inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            var intersects =
                ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y + Epsilon) + pi.X);

            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private static bool ContainsArcAngle(
        double startAngleRadians,
        double sweepAngleRadians,
        double targetAngleRadians)
    {
        var start = NormalizeAngle(startAngleRadians);
        var target = NormalizeAngle(targetAngleRadians);

        if (sweepAngleRadians > 0)
        {
            var delta = NormalizePositive(target - start);
            return delta <= sweepAngleRadians + Epsilon;
        }
        else
        {
            var delta = NormalizePositive(start - target);
            return delta <= -sweepAngleRadians + Epsilon;
        }
    }

    private static double NormalizeAngle(double radians)
    {
        var result = radians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    private static double NormalizePositive(double radians)
    {
        var result = radians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    private static void GuardTolerance(double tolerance)
    {
        if (tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));
    }
}
