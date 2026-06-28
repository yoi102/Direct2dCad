using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
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
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entity);
        GuardTolerance(tolerance);

        return HitTestEdgeCore(
            document,
            entity,
            point,
            tolerance,
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
        HashSet<BlockId> visitedBlocks,
        out CadHitTestResult result)
    {
        result = default;

        if (entity.IsErased)
            return false;

        switch (entity)
        {
            case CadLine line:
                return HitLineEdge(line, point, tolerance, out result);

            case CadArc arc:
                return HitArcEdge(arc, point, tolerance, out result);

            case CadCircle circle:
                return HitCircleEdge(circle, point, tolerance, out result);

            case CadPolyline polyline:
                return HitPolylineEdge(polyline, point, tolerance, out result);

            case CadBlockReference blockReference:
                return HitBlockReferenceEdge(
                    document,
                    blockReference,
                    point,
                    tolerance,
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

            case CadPolyline polyline:
                return HitPolylineFill(polyline, point, out result);

            case CadText text:
                return HitTextFill(text, point, out result);

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

    private static bool HitBlockReferenceEdge(
        CadDocument document,
        CadBlockReference blockReference,
        CadPointD worldPoint,
        double worldTolerance,
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
