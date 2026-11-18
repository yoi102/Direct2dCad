using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

public static class CadPlanarCurves
{
    private const string UnsupportedEntityMessage = "This operation supports lines, circular and elliptical arcs, and line/arc paths.";
    private const string UnsupportedSegmentMessage = "This operation requires line or circular arc segments.";

    /// <summary>Exact line, circular and elliptical primitives. Unsupported curves fail explicitly.</summary>
    public static IReadOnlyList<CadPlanarPrimitive> Get(CadEntity entity)
    {
        var primitives = Create(entity, out var error);
        return error is null ? primitives : throw new NotSupportedException(error);
    }

    /// <summary>Non-throwing <see cref="Get"/> for per-frame callers (object snap, measurement) that skip unsupported entities.</summary>
    public static bool TryGet(CadEntity entity, out IReadOnlyList<CadPlanarPrimitive> primitives)
    {
        primitives = Create(entity, out var error);
        return error is null;
    }

    private static IReadOnlyList<CadPlanarPrimitive> Create(CadEntity entity, out string? error)
    {
        error = null;
        switch (entity)
        {
            case CadRegion region: return region.Contours.SelectMany(c => c.Edges).ToArray();
            case CadLine line: return [CadPlanarPrimitive.Line(line.Start, line.End)];
            case CadCircle circle: return [CadPlanarPrimitive.Arc(circle.Center, circle.Radius, 0, 2 * Math.PI)];
            case CadArc arc: return [CadPlanarPrimitive.Arc(arc.Center, arc.Radius, arc.StartAngleRadians, arc.SweepAngleRadians)];
            case CadEllipse ellipse: return [CadPlanarPrimitive.EllipseArc(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.RotationRadians, 0, Math.PI * 2)];
            case CadEllipseArc arc: return [CadPlanarPrimitive.EllipseArc(arc.Center, arc.RadiusX, arc.RadiusY, arc.RotationRadians, arc.StartAngleRadians, arc.SweepAngleRadians)];
            case CadPolyline polyline:
                return Lines(polyline.Points, polyline.Closed);
            case CadRectangle rectangle when !rectangle.HasRoundedCorners:
                var b = rectangle.FrameBounds;
                return Lines(new CadPointD[]{new(b.MinX,b.MinY),new(b.MaxX,b.MinY),new(b.MaxX,b.MaxY),new(b.MinX,b.MaxY)}.Select(rectangle.GeometryTransform.TransformPoint).ToArray(), true);
            case CadCompositePath path:
                var primitives = new List<CadPlanarPrimitive>(); var start = path.StartPoint;
                foreach (var segment in path.Segments)
                {
                    var end = CadCompositePath.GetEndPoint(start, segment);
                    switch (segment)
                    {
                        case CadCompositeLineSegment: primitives.Add(CadPlanarPrimitive.Line(start, end)); break;
                        case CadCompositeArcSegment a: primitives.Add(CadPlanarPrimitive.Arc(a.Center, start.DistanceTo(a.Center), Math.Atan2(start.Y-a.Center.Y,start.X-a.Center.X),a.SweepAngleRadians)); break;
                        default: error = UnsupportedSegmentMessage; return [];
                    }
                    start = end;
                }
                if (path.Closed && !CadGeometryTolerance.Coincident(start,path.StartPoint)) primitives.Add(CadPlanarPrimitive.Line(start,path.StartPoint));
                return primitives;
            default: error = UnsupportedEntityMessage; return [];
        }
    }
    private static IReadOnlyList<CadPlanarPrimitive> Lines(IReadOnlyList<CadPointD> points, bool closed)
    {
        var result = new List<CadPlanarPrimitive>();
        for (var i = 1; i < points.Count; i++) result.Add(CadPlanarPrimitive.Line(points[i-1],points[i]));
        if (closed) result.Add(CadPlanarPrimitive.Line(points[^1],points[0]));
        return result;
    }
}
