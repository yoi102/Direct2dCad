using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Editor.Commands;

public sealed class PolygonSelectCommand : SelectionCommandBase
{
    private readonly CadPointD[] _polygon;
    private readonly CadRectD _queryBounds;
    private readonly CadSelectionMode _mode;
    private readonly bool _requireContained;
    private readonly Func<CadEntity, bool> _selectionFilter;
    private readonly double? _viewportZoom;
    private readonly BlockId _ownerBlockId;

    public override string Name => "Polygon Select";

    public PolygonSelectCommand(
        IEnumerable<CadPointD> polygon,
        CadSelectionMode mode = CadSelectionMode.Replace,
        bool requireContained = false,
        Func<CadEntity, bool>? selectionFilter = null,
        double? viewportZoom = null,
        BlockId? ownerBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        _polygon = polygon.ToArray();
        if (_polygon.Length < 3 || _polygon.Any(point => !IsFinite(point)))
            throw new ArgumentException("Selection polygon must contain at least three finite points.", nameof(polygon));

        _queryBounds = _polygon.Aggregate(CadRectD.Empty, static (bounds, point) => bounds.ExpandToInclude(point));
        if (_queryBounds.IsEmpty)
            throw new ArgumentException("Selection polygon cannot be empty.", nameof(polygon));

        _mode = mode;
        _requireContained = requireContained;
        _selectionFilter = selectionFilter ?? (_ => true);
        _viewportZoom = viewportZoom is > 0 && double.IsFinite(viewportZoom.Value)
            ? viewportZoom
            : null;
        _ownerBlockId = ownerBlockId ?? BlockId.ModelSpace;
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        var options = new CadHitTestOptions(_viewportZoom ?? context.Viewport.Zoom);
        var queryArea = _queryBounds.Inflate(context.HitTesting.GetMaxStrokeHitPadding(options));
        var entityIds = context.SpatialIndex.Query(_ownerBlockId, queryArea)
            .Where(entityId =>
                context.Document.TryGetEntity(entityId, out var entity) &&
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
        var hitBounds = context.HitTesting.GetHitTestEntityBounds(entity, options);
        if (_requireContained)
            return GetCorners(hitBounds).All(PointInSelectionPolygon);

        var paddedBounds = hitBounds.Inflate(context.HitTesting.GetStrokeHitPadding(entity, options));
        if (!PolygonTouchesRectangle(_polygon, paddedBounds))
            return false;

        return EntityTouchesPolygon(entity);
    }

    private bool EntityTouchesPolygon(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => PolylineTouchesPolygon([line.Start, line.End], closed: false),
            CadPolyline polyline => PolylineTouchesPolygon(polyline.Points, polyline.Closed),
            CadSpline spline => PolylineTouchesPolygon(
                spline.EnumerateFlattenedPoints(24).ToArray(),
                spline.Closed),
            CadCircle circle => PolylineTouchesPolygon(
                SampleEllipse(circle.Center, circle.Radius, circle.Radius, 72),
                closed: true),
            CadEllipse ellipse => PolylineTouchesPolygon(
                SampleEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, 72),
                closed: true),
            CadArc arc => PolylineTouchesPolygon(
                SampleArc(arc.Center, arc.Radius, arc.Radius, arc.StartAngleRadians, arc.SweepAngleRadians),
                arc.IsFullCircle),
            CadEllipseArc ellipseArc => PolylineTouchesPolygon(
                SampleArc(
                    ellipseArc.Center,
                    ellipseArc.RadiusX,
                    ellipseArc.RadiusY,
                    ellipseArc.StartAngleRadians,
                    ellipseArc.SweepAngleRadians),
                closed: false),
            CadImage image => PolylineTouchesPolygon(image.GetFrameCorners(), closed: true),
            _ => PolylineTouchesPolygon(GetCorners(entity.Bounds), closed: true)
        };
    }

    private bool PolylineTouchesPolygon(IReadOnlyList<CadPointD> points, bool closed)
    {
        if (points.Count == 0)
            return false;
        if (points.Any(PointInSelectionPolygon))
            return true;

        for (var index = 1; index < points.Count; index++)
        {
            if (SegmentTouchesSelectionPolygon(points[index - 1], points[index]))
                return true;
        }

        if (closed)
        {
            if (points.Count > 1 && SegmentTouchesSelectionPolygon(points[^1], points[0]))
                return true;
            if (_polygon.Any(point => PointInPolygon(point, points)))
                return true;
        }

        return false;
    }

    private bool SegmentTouchesSelectionPolygon(CadPointD start, CadPointD end)
    {
        for (var index = 0; index < _polygon.Length; index++)
        {
            if (SegmentsIntersect(start, end, _polygon[index], _polygon[(index + 1) % _polygon.Length]))
                return true;
        }

        return false;
    }

    private bool PointInSelectionPolygon(CadPointD point) => PointInPolygon(point, _polygon);

    private static IReadOnlyList<CadPointD> SampleEllipse(
        CadPointD center,
        double radiusX,
        double radiusY,
        int segmentCount)
    {
        var points = new CadPointD[segmentCount];
        for (var index = 0; index < segmentCount; index++)
        {
            var angle = Math.PI * 2 * index / segmentCount;
            points[index] = new CadPointD(
                center.X + radiusX * Math.Cos(angle),
                center.Y + radiusY * Math.Sin(angle));
        }
        return points;
    }

    private static IReadOnlyList<CadPointD> SampleArc(
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngle,
        double sweepAngle)
    {
        var segmentCount = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI * 2) * 72));
        var points = new CadPointD[segmentCount + 1];
        for (var index = 0; index <= segmentCount; index++)
        {
            var angle = startAngle + sweepAngle * index / segmentCount;
            points[index] = new CadPointD(
                center.X + radiusX * Math.Cos(angle),
                center.Y + radiusY * Math.Sin(angle));
        }
        return points;
    }

    private static CadPointD[] GetCorners(CadRectD bounds) =>
    [
        new CadPointD(bounds.MinX, bounds.MinY),
        new CadPointD(bounds.MaxX, bounds.MinY),
        new CadPointD(bounds.MaxX, bounds.MaxY),
        new CadPointD(bounds.MinX, bounds.MaxY)
    ];

    private static bool PolygonTouchesRectangle(IReadOnlyList<CadPointD> polygon, CadRectD rectangle)
    {
        if (rectangle.IsEmpty)
            return false;
        var rectanglePoints = GetCorners(rectangle);
        if (polygon.Any(rectangle.Contains) || rectanglePoints.Any(point => PointInPolygon(point, polygon)))
            return true;

        for (var polygonIndex = 0; polygonIndex < polygon.Count; polygonIndex++)
        {
            var polygonStart = polygon[polygonIndex];
            var polygonEnd = polygon[(polygonIndex + 1) % polygon.Count];
            for (var rectangleIndex = 0; rectangleIndex < rectanglePoints.Length; rectangleIndex++)
            {
                if (SegmentsIntersect(
                        polygonStart,
                        polygonEnd,
                        rectanglePoints[rectangleIndex],
                        rectanglePoints[(rectangleIndex + 1) % rectanglePoints.Length]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool PointInPolygon(CadPointD point, IReadOnlyList<CadPointD> polygon)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            if (PointOnSegment(a, point, b))
                return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static bool SegmentsIntersect(CadPointD a, CadPointD b, CadPointD c, CadPointD d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);
        if (o1 == 0 && PointOnSegment(a, c, b)) return true;
        if (o2 == 0 && PointOnSegment(a, d, b)) return true;
        if (o3 == 0 && PointOnSegment(c, a, d)) return true;
        if (o4 == 0 && PointOnSegment(c, b, d)) return true;
        return o1 != o2 && o3 != o4;
    }

    private static int Orientation(CadPointD a, CadPointD b, CadPointD c)
    {
        const double epsilon = 1e-12;
        var value = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return Math.Abs(value) <= epsilon ? 0 : value > 0 ? 1 : -1;
    }

    private static bool PointOnSegment(CadPointD a, CadPointD point, CadPointD b)
    {
        const double epsilon = 1e-12;
        return Orientation(a, b, point) == 0 &&
               point.X >= Math.Min(a.X, b.X) - epsilon &&
               point.X <= Math.Max(a.X, b.X) + epsilon &&
               point.Y >= Math.Min(a.Y, b.Y) - epsilon &&
               point.Y <= Math.Max(a.Y, b.Y) + epsilon;
    }

    private static bool IsFinite(CadPointD point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);
}
