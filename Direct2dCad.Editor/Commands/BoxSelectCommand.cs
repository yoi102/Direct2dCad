using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Commands;

public sealed class BoxSelectCommand : SelectionCommandBase
{
    private readonly CadRectD _worldArea;
    private readonly CadSelectionMode _mode;
    private readonly bool _requireContained;

    public override string Name => "Box Select";

    public BoxSelectCommand(
        CadRectD worldArea,
        CadSelectionMode mode = CadSelectionMode.Replace,
        bool requireContained = false)
    {
        if (worldArea.IsEmpty)
            throw new ArgumentException("Selection area cannot be empty.", nameof(worldArea));

        _worldArea = worldArea;
        _mode = mode;
        _requireContained = requireContained;
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        var entityIds = context.SpatialIndex.Query(_worldArea)
            .Where(entityId => context.Document.TryGetEntity(entityId, out var entity) &&
                               entity is not null &&
                               IsSelectionMatch(entity))
            .ToArray();

        ApplySelection(context.Selection, entityIds, _mode);
    }

    private bool IsSelectionMatch(CadEntity entity)
    {
        return _requireContained
            ? _worldArea.Contains(entity.Bounds)
            : EntityTouchesArea(entity, _worldArea);
    }

    private static bool EntityTouchesArea(CadEntity entity, CadRectD area)
    {
        if (area.Contains(entity.Bounds))
            return true;

        return entity switch
        {
            CadLine line => SegmentIntersectsArea(line.Start, line.End, area),
            CadPolyline polyline => PolylineIntersectsArea(polyline, area),
            CadCircle circle => CircleIntersectsArea(circle.Center, circle.Radius, area),
            CadArc arc => ArcIntersectsArea(arc, area),
            _ => area.Intersects(entity.Bounds)
        };
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

    private static bool ArcContainsAngle(CadArc arc, double angle)
    {
        const double twoPi = Math.PI * 2.0;
        const double epsilon = 1e-12;

        if (arc.IsFullCircle)
            return true;

        var start = NormalizeAngle(arc.StartAngleRadians);
        var target = NormalizeAngle(angle);

        if (arc.SweepAngleRadians > 0)
            return NormalizePositive(target - start) <= arc.SweepAngleRadians + epsilon;

        return NormalizePositive(start - target) <= -arc.SweepAngleRadians + epsilon;

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
