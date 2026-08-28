using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Editor.Commands;

public sealed class BoxSelectCommand : SelectionCommandBase
{
    private readonly CadRectD _worldArea;
    private readonly CadSelectionMode _mode;
    private readonly bool _requireContained;
    private readonly Func<CadEntity, bool> _selectionFilter;
    private readonly double? _viewportZoom;
    private readonly BlockId _ownerBlockId;
    private readonly CadHitTestOptions? _hitTestOptions;

    public override string Name => "Box Select";

    public BoxSelectCommand(
        CadRectD worldArea,
        CadSelectionMode mode = CadSelectionMode.Replace,
        bool requireContained = false,
        Func<CadEntity, bool>? selectionFilter = null,
        double? viewportZoom = null,
        BlockId? ownerBlockId = null,
        CadHitTestOptions? hitTestOptions = null)
    {
        if (worldArea.IsEmpty)
            throw new ArgumentException("Selection area cannot be empty.", nameof(worldArea));

        _worldArea = worldArea;
        _mode = mode;
        _requireContained = requireContained;
        _selectionFilter = selectionFilter ?? (_ => true);
        _viewportZoom = viewportZoom is > 0 && double.IsFinite(viewportZoom.Value)
            ? viewportZoom
            : null;
        _ownerBlockId = ownerBlockId ?? BlockId.ModelSpace;
        _hitTestOptions = hitTestOptions;
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        var options = _hitTestOptions ??
                      new CadHitTestOptions(_viewportZoom ?? context.Viewport.Zoom);
        var queryArea = _worldArea.Inflate(context.HitTesting.GetMaxStrokeHitPadding(options));
        var entityIds = context.SpatialIndex.Query(_ownerBlockId, queryArea)
            .Where(entityId => context.Document.TryGetEntity(entityId, out var entity) &&
                               entity is not null &&
                               _selectionFilter(entity) &&
                               IsSelectionMatch(context, entity, options))
            .ToArray();

        ApplySelection(context.Selection, entityIds, _mode);
    }

    private bool IsSelectionMatch(
        CadEditorCommandContext context,
        CadEntity entity,
        CadHitTestOptions options)
    {
        return _requireContained
            ? _worldArea.Contains(context.HitTesting.GetHitTestEntityBounds(entity, options))
            : EntityTouchesArea(
                entity,
                _worldArea.Inflate(context.HitTesting.GetStrokeHitPadding(entity, options)));
    }

    private static bool EntityTouchesArea(CadEntity entity, CadRectD area)
    {
        if (area.Contains(entity.Bounds))
            return true;

        return entity switch
        {
            CadLine line => SegmentIntersectsArea(line.Start, line.End, area),
            CadPolyline polyline => PolylineIntersectsArea(polyline, area),
            CadSpline spline => SplineIntersectsArea(spline, area),
            CadCircle circle => CircleIntersectsArea(circle.Center, circle.Radius, area),
            CadEllipse ellipse => EllipseIntersectsArea(ellipse, area),
            CadEllipseArc ellipseArc => EllipseArcIntersectsArea(ellipseArc, area),
            CadArc arc => ArcIntersectsArea(arc, area),
            CadImage image => ImageIntersectsArea(image, area),
            _ => area.Intersects(entity.Bounds)
        };
    }

    private static bool ImageIntersectsArea(CadImage image, CadRectD area)
    {
        var corners = image.GetFrameCorners();
        if (corners.Any(area.Contains))
            return true;

        var areaCorners = new[]
        {
            new CadPointD(area.MinX, area.MinY),
            new CadPointD(area.MaxX, area.MinY),
            new CadPointD(area.MaxX, area.MaxY),
            new CadPointD(area.MinX, area.MaxY)
        };
        if (areaCorners.Any(point => image.FrameBounds.Contains(image.WorldToFrame(point))))
            return true;

        for (var index = 0; index < corners.Count; index++)
        {
            if (SegmentIntersectsArea(corners[index], corners[(index + 1) % corners.Count], area))
                return true;
        }

        return false;
    }

    private static bool PolylineIntersectsArea(CadPolyline polyline, CadRectD area)
    {
        var points = polyline.Points;
        if (points.Count < 2)
            return false;

        for (var i = 1; i < points.Count; i++)
        {
            if (SegmentIntersectsArea(points[i - 1], points[i], area))
                return true;
        }

        return polyline.Closed &&
               SegmentIntersectsArea(points[^1], points[0], area);
    }

    private static bool SplineIntersectsArea(CadSpline spline, CadRectD area)
    {
        using var points = spline.EnumerateFlattenedPoints(24).GetEnumerator();
        if (!points.MoveNext())
            return false;

        var previous = points.Current;
        while (points.MoveNext())
        {
            var current = points.Current;
            if (SegmentIntersectsArea(previous, current, area))
                return true;
            previous = current;
        }

        return false;
    }

    private static bool ArcIntersectsArea(CadArc arc, CadRectD area)
    {
        if (arc.IsFullCircle)
            return CircleIntersectsArea(arc.Center, arc.Radius, area);

        if (area.Contains(arc.StartPoint) || area.Contains(arc.EndPoint))
            return true;

        foreach (var point in EnumerateCircleRectIntersectionPoints(arc.Center, arc.Radius, area))
        {
            var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X);
            if (ArcContainsAngle(arc, angle))
                return true;
        }

        return false;
    }

    private static bool CircleIntersectsArea(CadPointD center, double radius, CadRectD area)
    {
        var radiusSquared = radius * radius;
        var closest = new CadPointD(
            Clamp(center.X, area.MinX, area.MaxX),
            Clamp(center.Y, area.MinY, area.MaxY));
        var minDistanceSquared = DistanceSquared(center, closest);
        var maxDistanceSquared = Math.Max(
            Math.Max(
                DistanceSquared(center, new CadPointD(area.MinX, area.MinY)),
                DistanceSquared(center, new CadPointD(area.MinX, area.MaxY))),
            Math.Max(
                DistanceSquared(center, new CadPointD(area.MaxX, area.MinY)),
                DistanceSquared(center, new CadPointD(area.MaxX, area.MaxY))));

        return minDistanceSquared <= radiusSquared + 1e-9 &&
               maxDistanceSquared >= radiusSquared - 1e-9;
    }

    private static bool EllipseIntersectsArea(CadEllipse ellipse, CadRectD area)
    {
        if (!ellipse.Bounds.Intersects(area))
            return false;

        if (area.Contains(new CadPointD(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y)) ||
            area.Contains(new CadPointD(ellipse.Center.X, ellipse.Center.Y + ellipse.RadiusY)) ||
            area.Contains(new CadPointD(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y)) ||
            area.Contains(new CadPointD(ellipse.Center.X, ellipse.Center.Y - ellipse.RadiusY)))
        {
            return true;
        }

        return EnumerateEllipseRectIntersectionPoints(ellipse, area).Any();
    }

    private static bool EllipseArcIntersectsArea(CadEllipseArc ellipseArc, CadRectD area)
    {
        if (!ellipseArc.Bounds.Intersects(area))
            return false;

        if (area.Contains(ellipseArc.StartPoint) || area.Contains(ellipseArc.EndPoint))
            return true;

        foreach (var point in EnumerateEllipseRectIntersectionPoints(
            ellipseArc.Center,
            ellipseArc.RadiusX,
            ellipseArc.RadiusY,
            area))
        {
            var angle = Math.Atan2(
                (point.Y - ellipseArc.Center.Y) / ellipseArc.RadiusY,
                (point.X - ellipseArc.Center.X) / ellipseArc.RadiusX);
            if (ContainsAngleOnSweep(
                ellipseArc.StartAngleRadians,
                ellipseArc.SweepAngleRadians,
                angle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentIntersectsArea(CadPointD start, CadPointD end, CadRectD area)
    {
        if (area.Contains(start) || area.Contains(end))
            return true;

        var bottomLeft = new CadPointD(area.MinX, area.MinY);
        var bottomRight = new CadPointD(area.MaxX, area.MinY);
        var topRight = new CadPointD(area.MaxX, area.MaxY);
        var topLeft = new CadPointD(area.MinX, area.MaxY);

        return SegmentsIntersect(start, end, bottomLeft, bottomRight) ||
               SegmentsIntersect(start, end, bottomRight, topRight) ||
               SegmentsIntersect(start, end, topRight, topLeft) ||
               SegmentsIntersect(start, end, topLeft, bottomLeft);
    }

    private static IEnumerable<CadPointD> EnumerateCircleRectIntersectionPoints(CadPointD center, double radius, CadRectD area)
    {
        foreach (var x in new[] { area.MinX, area.MaxX })
        {
            var dx = x - center.X;
            var remainder = radius * radius - dx * dx;
            if (remainder < 0)
                continue;

            var dy = Math.Sqrt(Math.Max(0, remainder));
            var y1 = center.Y - dy;
            var y2 = center.Y + dy;
            if (y1 >= area.MinY && y1 <= area.MaxY)
                yield return new CadPointD(x, y1);
            if (dy > 0 && y2 >= area.MinY && y2 <= area.MaxY)
                yield return new CadPointD(x, y2);
        }

        foreach (var y in new[] { area.MinY, area.MaxY })
        {
            var dy = y - center.Y;
            var remainder = radius * radius - dy * dy;
            if (remainder < 0)
                continue;

            var dx = Math.Sqrt(Math.Max(0, remainder));
            var x1 = center.X - dx;
            var x2 = center.X + dx;
            if (x1 >= area.MinX && x1 <= area.MaxX)
                yield return new CadPointD(x1, y);
            if (dx > 0 && x2 >= area.MinX && x2 <= area.MaxX)
                yield return new CadPointD(x2, y);
        }
    }

    private static IEnumerable<CadPointD> EnumerateEllipseRectIntersectionPoints(CadEllipse ellipse, CadRectD area)
    {
        return EnumerateEllipseRectIntersectionPoints(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, area);
    }

    private static IEnumerable<CadPointD> EnumerateEllipseRectIntersectionPoints(
        CadPointD center,
        double radiusX,
        double radiusY,
        CadRectD area)
    {
        foreach (var x in new[] { area.MinX, area.MaxX })
        {
            var normalizedX = (x - center.X) / radiusX;
            var remainder = 1.0 - normalizedX * normalizedX;
            if (remainder < 0)
                continue;

            var dy = radiusY * Math.Sqrt(Math.Max(0, remainder));
            var y1 = center.Y - dy;
            var y2 = center.Y + dy;
            if (y1 >= area.MinY && y1 <= area.MaxY)
                yield return new CadPointD(x, y1);
            if (dy > 0 && y2 >= area.MinY && y2 <= area.MaxY)
                yield return new CadPointD(x, y2);
        }

        foreach (var y in new[] { area.MinY, area.MaxY })
        {
            var normalizedY = (y - center.Y) / radiusY;
            var remainder = 1.0 - normalizedY * normalizedY;
            if (remainder < 0)
                continue;

            var dx = radiusX * Math.Sqrt(Math.Max(0, remainder));
            var x1 = center.X - dx;
            var x2 = center.X + dx;
            if (x1 >= area.MinX && x1 <= area.MaxX)
                yield return new CadPointD(x1, y);
            if (dx > 0 && x2 >= area.MinX && x2 <= area.MaxX)
                yield return new CadPointD(x2, y);
        }
    }

    private static bool ArcContainsAngle(CadArc arc, double angle)
    {
        return ContainsAngleOnSweep(arc.StartAngleRadians, arc.SweepAngleRadians, angle, arc.IsFullCircle);
    }

    private static bool ContainsAngleOnSweep(
        double startAngleRadians,
        double sweepAngleRadians,
        double angle,
        bool isFullCircle = false)
    {
        const double twoPi = Math.PI * 2.0;
        const double epsilon = 1e-12;

        if (isFullCircle)
            return true;

        var start = NormalizeAngle(startAngleRadians);
        var target = NormalizeAngle(angle);

        if (sweepAngleRadians > 0)
            return NormalizePositive(target - start) <= sweepAngleRadians + epsilon;

        return NormalizePositive(start - target) <= -sweepAngleRadians + epsilon;

        static double NormalizeAngle(double radians)
        {
            var result = radians % twoPi;
            return result < 0 ? result + twoPi : result;
        }

        static double NormalizePositive(double radians)
        {
            var result = radians % twoPi;
            return result < 0 ? result + twoPi : result;
        }
    }

    private static bool SegmentsIntersect(CadPointD a, CadPointD b, CadPointD c, CadPointD d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);

        if (o1 == 0 && IsPointOnSegment(a, c, b))
            return true;
        if (o2 == 0 && IsPointOnSegment(a, d, b))
            return true;
        if (o3 == 0 && IsPointOnSegment(c, a, d))
            return true;
        if (o4 == 0 && IsPointOnSegment(c, b, d))
            return true;

        return o1 != o2 && o3 != o4;
    }

    private static int Orientation(CadPointD a, CadPointD b, CadPointD c)
    {
        const double epsilon = 1e-12;
        var value = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(value) <= epsilon)
            return 0;

        return value > 0 ? 1 : -1;
    }

    private static bool IsPointOnSegment(CadPointD a, CadPointD point, CadPointD b)
    {
        const double epsilon = 1e-12;
        return point.X >= Math.Min(a.X, b.X) - epsilon &&
               point.X <= Math.Max(a.X, b.X) + epsilon &&
               point.Y >= Math.Min(a.Y, b.Y) - epsilon &&
               point.Y <= Math.Max(a.Y, b.Y) + epsilon;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static double DistanceSquared(CadPointD a, CadPointD b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
