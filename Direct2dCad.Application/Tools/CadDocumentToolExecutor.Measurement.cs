using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private string MeasureGeometry(JsonElement arguments)
    {
        var operation = (OptionalString(arguments, "operation") ?? "measure").ToLowerInvariant();
        if (operation is not "measure")
            return ExecuteAdvancedMeasurement(operation, arguments);

        var pointResults = Array.Empty<object>();
        if (arguments.TryGetProperty("points", out _))
        {
            var points = RequiredPoints(arguments, "points", 2);
            var segments = new List<object>();
            var total = 0.0;
            for (var index = 1; index < points.Length; index++)
            {
                var distance = points[index - 1].DistanceTo(points[index]);
                total += distance;
                segments.Add(new
                {
                    start_index = index - 1,
                    end_index = index,
                    distance_millimeters = distance,
                    angle_degrees = Math.Atan2(
                        points[index].Y - points[index - 1].Y,
                        points[index].X - points[index - 1].X) * 180.0 / Math.PI
                });
            }
            pointResults =
            [
                new
                {
                    point_count = points.Length,
                    total_distance_millimeters = total,
                    segments
                }
            ];
        }

        var entityIds = arguments.TryGetProperty("entity_ids", out _) ||
                        session.CadEditor.Selection.Count > 0
            ? ResolveEntityIds(arguments, allowSelectionFallback: true)
            : [];
        var document = session.CadEditor.Document;
        var entities = entityIds.Select(document.GetEntity).Select(MeasureEntity).ToArray();
        return Success(new { points = pointResults, entities });
    }

    private string ExecuteAdvancedMeasurement(string operation, JsonElement arguments)
    {
        var entityIds = ResolveEntityIds(arguments, allowSelectionFallback: true);
        var entities = entityIds
            .Select(session.CadEditor.Document.GetEntity)
            .ToArray();
        return operation switch
        {
            "intersections" => Success(new
            {
                operation,
                approximate = entities.Any(entity => QueryPrimitives(entity).Approximate),
                curve_tessellation_error_bound_millimeters=entities.Select(entity=>QueryPrimitives(entity).Error).DefaultIfEmpty().Max(),
                points = FindIntersections(entities)
                    .Select(point => new { x = point.X, y = point.Y })
                    .ToArray()
            }),
            "nearest_point" or "project_point" => ExecuteNearestPoint(operation, arguments, entities),
            "chord" => ExecuteChord(arguments, entities),
            _ => throw new ArgumentException("operation must be measure, intersections, nearest_point, project_point, or chord.")
        };
    }

    private static string ExecuteNearestPoint(
        string operation,
        JsonElement arguments,
        IReadOnlyList<CadEntity> entities)
    {
        var point = RequiredPoint(arguments, "point");
        var geometry=entities.Select(QueryPrimitives).ToArray();
        var candidates = geometry
            .SelectMany(g=>g.Primitives.Select(p=>ProjectToPrimitive(point,p)))
            .OrderBy(candidate => candidate.DistanceSquared)
            .ToArray();
        if (candidates.Length == 0)
            throw new ArgumentException("The selected entities have no measurable segments.");
        var nearest = candidates[0];
        return Success(new
        {
            operation,
            approximate = geometry.Any(g=>g.Approximate),
            distance_error_bound_millimeters=geometry.Select(g=>g.Error).DefaultIfEmpty().Max(),
            source_point = new { x = point.X, y = point.Y },
            projected_point = new { x = nearest.Point.X, y = nearest.Point.Y },
            distance_millimeters = Math.Sqrt(nearest.DistanceSquared)
        });
    }

    private static string ExecuteChord(JsonElement arguments, IReadOnlyList<CadEntity> entities)
    {
        if (entities.Count != 1)
            throw new ArgumentException("chord requires exactly one entity.");
        var entity = entities[0];
        CadPointD start;
        CadPointD end;
        var approximate = false;
        switch (entity)
        {
            case CadCircle circle:
                start = circle.Center + new CadVectorD(
                    circle.Radius * Math.Cos(RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0),
                    circle.Radius * Math.Sin(RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0));
                end = circle.Center + new CadVectorD(
                    circle.Radius * Math.Cos(RequiredDouble(arguments, "end_angle_degrees") * Math.PI / 180.0),
                    circle.Radius * Math.Sin(RequiredDouble(arguments, "end_angle_degrees") * Math.PI / 180.0));
                break;
            case CadArc arc:
                start = arc.StartPoint;
                end = arc.EndPoint;
                break;
            case CadEllipseArc ellipseArc:
                start = ellipseArc.StartPoint;
                end = ellipseArc.EndPoint;
                break;
            default:
                var points = FlattenEntity(entity);
                if (points.Count < 2)
                    throw new ArgumentException("The entity has no measurable chord.");
                start = points[0];
                end = points[^1];
                approximate = entity is not CadLine;
                break;
        }
        return Success(new
        {
            operation = "chord",
            approximate,
            start = new { x = start.X, y = start.Y },
            end = new { x = end.X, y = end.Y },
            distance_millimeters = start.DistanceTo(end),
            angle_degrees = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI
        });
    }

    private static IReadOnlyList<CadPointD> FindIntersections(IReadOnlyList<CadEntity> entities)
    {
        var points = new List<CadPointD>();
        var geometry=entities.Select(QueryPrimitives).ToArray();var pairs=0;
        for (var leftIndex = 0; leftIndex < entities.Count; leftIndex++)
        {
            var leftSegments = geometry[leftIndex].Primitives;
            for (var rightIndex = leftIndex + 1; rightIndex < entities.Count; rightIndex++)
            {
                var rightSegments = geometry[rightIndex].Primitives;
                foreach (var left in leftSegments)
                foreach (var right in rightSegments)
                {
                    if(++pairs>1000000) throw new InvalidOperationException("Intersection calculation exceeds its budget; query fewer entities.");
                    foreach(var point in CadPlanarGeometry.Intersections(left,right))
                        if(points.All(existing=>!CadGeometryTolerance.Coincident(existing,point))) points.Add(point);
                }
            }
        }
        return points;
    }

    private static (IReadOnlyList<CadPlanarPrimitive> Primitives,bool Approximate,double Error) QueryPrimitives(CadEntity entity)
    {
        if(CadPlanarCurves.TryGet(entity,out var exact)) return (exact,false,0);
        // Non-curves keep the original NotSupportedException from Get.
        if(entity is not Curve curve) return (CadPlanarCurves.Get(entity),false,0);
        var tessellation=CadCurveTessellation.Create(curve);
        if(tessellation.ReachedBudget) throw new InvalidOperationException("Curve calculation exceeds its precision budget.");
        return (tessellation.Points.Zip(tessellation.Points.Skip(1),CadPlanarPrimitive.Line).ToArray(),true,tessellation.ErrorBound);
    }

    private static SegmentProjection ProjectToPrimitive(CadPointD point,CadPlanarPrimitive p)
    {
        var nearest=p.NearestPoint(point);return new(nearest,nearest.DistanceSquaredTo(point));
    }

    private static IReadOnlyList<CadPointD> FlattenEntity(CadEntity entity)
    {
        return entity is Curve curve ? CadCurveTessellation.Create(curve).Points : [];
    }

    private static IReadOnlyList<CadPointD> CloseIfNeeded(IReadOnlyList<CadPointD> points, bool closed) =>
        closed && points.Count > 0 && points[0] != points[^1]
            ? points.Concat([points[0]]).ToArray()
            : points;

    private static IReadOnlyList<CadPointD> SampleCircle(CadPointD center, double radius, double start, double sweep) =>
        SampleParametric(center, radius, radius, start, sweep);

    private static IReadOnlyList<CadPointD> SampleEllipse(CadPointD center, double radiusX, double radiusY, double start, double sweep) =>
        SampleParametric(center, radiusX, radiusY, start, sweep);

    private static IReadOnlyList<CadPointD> SampleParametric(CadPointD center, double radiusX, double radiusY, double start, double sweep)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 24)), 8, 192);
        return Enumerable.Range(0, count + 1)
            .Select(index =>
            {
                var angle = start + sweep * index / count;
                return new CadPointD(center.X + radiusX * Math.Cos(angle), center.Y + radiusY * Math.Sin(angle));
            })
            .ToArray();
    }

    private static SegmentProjection ProjectToSegment(CadPointD point, CadPointD start, CadPointD end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= 1e-18
            ? 0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        var projected = new CadPointD(start.X + dx * t, start.Y + dy * t);
        return new SegmentProjection(projected, projected.DistanceSquaredTo(point));
    }

    private static bool TryIntersectSegments(CadPointD a, CadPointD b, CadPointD c, CadPointD d, out CadPointD point)
    {
        var denominator = (a.X - b.X) * (c.Y - d.Y) - (a.Y - b.Y) * (c.X - d.X);
        if (Math.Abs(denominator) <= 1e-12)
        {
            point = default;
            return false;
        }
        var first = a.X * b.Y - a.Y * b.X;
        var second = c.X * d.Y - c.Y * d.X;
        point = new CadPointD(
            (first * (c.X - d.X) - (a.X - b.X) * second) / denominator,
            (first * (c.Y - d.Y) - (a.Y - b.Y) * second) / denominator);
        return IsBetween(point, a, b) && IsBetween(point, c, d);
    }

    private static bool IsBetween(CadPointD point, CadPointD start, CadPointD end) =>
        point.X >= Math.Min(start.X, end.X) - 1e-9 && point.X <= Math.Max(start.X, end.X) + 1e-9 &&
        point.Y >= Math.Min(start.Y, end.Y) - 1e-9 && point.Y <= Math.Max(start.Y, end.Y) + 1e-9;

    private readonly record struct SegmentProjection(CadPointD Point, double DistanceSquared);

    private static object MeasureEntity(CadEntity entity)
    {
        var curve = entity as Curve;
        var measurement = curve is not null ? CadCurveMeasurements.Measure(curve) : (CadCurveMeasurement?)null;
        var area = measurement?.Area;
        var result = new Dictionary<string, object?>
        {
            ["entity_id"] = entity.Id.Value,
            ["type"] = entity.GetType().Name[3..],
            ["bounds"] = RectDto(entity.Bounds),
            ["length_millimeters"] = measurement?.Length,
            ["approximate"] = measurement?.Approximate ?? false,
            ["length_error_estimate_millimeters"] = measurement?.LengthErrorEstimate,
            ["reached_calculation_budget"] = measurement?.ReachedBudget ?? false,
            ["closed"] = curve?.IsClosed,
            ["area_square_millimeters"] = area
        };
        switch (entity)
        {
            case CadLine line:
                result["start"] = new { x = line.Start.X, y = line.Start.Y };
                result["end"] = new { x = line.End.X, y = line.End.Y };
                result["angle_degrees"] = Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;
                break;
            case CadCircle circle:
                result["center"] = new { x = circle.Center.X, y = circle.Center.Y };
                result["radius_millimeters"] = circle.Radius;
                result["circumference_millimeters"] = circle.Length;
                break;
            case CadArc arc:
                result["center"] = new { x = arc.Center.X, y = arc.Center.Y };
                result["radius_millimeters"] = arc.Radius;
                result["start_angle_degrees"] = arc.StartAngleDegrees;
                result["sweep_angle_degrees"] = arc.SweepAngleDegrees;
                break;
            case CadEllipse ellipse:
                result["center"] = new { x = ellipse.Center.X, y = ellipse.Center.Y };
                result["radius_x_millimeters"] = ellipse.RadiusX;
                result["radius_y_millimeters"] = ellipse.RadiusY;
                break;
            case CadEllipseArc ellipseArc:
                result["center"] = new { x = ellipseArc.Center.X, y = ellipseArc.Center.Y };
                result["radius_x_millimeters"] = ellipseArc.RadiusX;
                result["radius_y_millimeters"] = ellipseArc.RadiusY;
                result["start_angle_degrees"] = ellipseArc.StartAngleDegrees;
                result["sweep_angle_degrees"] = ellipseArc.SweepAngleDegrees;
                break;
        }
        return result;
    }

    private static double? TryGetArea(CadEntity entity) => entity switch
    {
        CadCircle circle => Math.PI * circle.Radius * circle.Radius,
        CadEllipse ellipse => Math.PI * ellipse.RadiusX * ellipse.RadiusY,
        CadRectangle rectangle => rectangle.HasRoundedCorners
            ? rectangle.Bounds.Width * rectangle.Bounds.Height - (4 - Math.PI) * rectangle.CornerRadiusX * rectangle.CornerRadiusY
            : rectangle.Bounds.Width * rectangle.Bounds.Height,
        CadArc arc when arc.IsFullCircle => Math.PI * arc.Radius * arc.Radius,
        CadPolyline polyline when polyline.Closed => PolygonArea(polyline.Points),
        CadSpline spline when spline.Closed => PolygonArea(spline.EnumerateFlattenedPoints().ToArray()),
        CadCompositePath path when path.Closed => PolygonArea(path.EnumerateFlattenedPoints().ToArray()),
        _ => null
    };

    private static double PolygonArea(IReadOnlyList<CadPointD> points)
    {
        if (points.Count < 3)
            return 0;
        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }
        return Math.Abs(area) * 0.5;
    }
}
