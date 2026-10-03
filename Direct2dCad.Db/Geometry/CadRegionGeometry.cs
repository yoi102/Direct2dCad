using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

public static class CadRegionGeometry
{
    // Split arcs at their vertical extrema so the ray rule has half-open, monotone spans.
    public static bool Contains(IEnumerable<CadRegionContour> contours, CadPointD point) =>
        contours.Count(c => Contains(c.Edges, point)) % 2 != 0;

    public static bool Contains(IReadOnlyList<CadPlanarPrimitive> edges, CadPointD point)
    {
        var inside = false;
        foreach (var p in edges)
        {
            var tolerance = CadGeometryTolerance.For(Math.Max(Math.Abs(point.Y), Math.Max(Math.Abs(p.Start.Y), Math.Abs(p.End.Y))));
            if (p.IsLine)
            {
                if ((p.Start.Y > point.Y + tolerance) != (p.End.Y > point.Y + tolerance) &&
                    p.Start.X + (point.Y - p.Start.Y) * (p.End.X - p.Start.X) / (p.End.Y - p.Start.Y) > point.X) inside = !inside;
                continue;
            }
            var cuts = new List<double> { 0, 1 };
            foreach (var angle in new[] { Math.PI / 2, Math.PI * 1.5 })
            {
                var t = p.Parameter(CadPlanarPrimitive.Point(p.Center, p.Radius, angle));
                if (t > 1e-12 && t < 1 - 1e-12) cuts.Add(t);
            }
            cuts.Sort();
            for (var i = 1; i < cuts.Count; i++)
            {
                var lo = cuts[i - 1]; var hi = cuts[i]; var a = p.At(lo); var b = p.At(hi);
                if ((a.Y > point.Y + tolerance) == (b.Y > point.Y + tolerance)) continue;
                var sine = Math.Clamp((point.Y - p.Center.Y) / p.Radius, -1, 1);
                var cosineSign = Math.Sign(Math.Cos(p.StartAngle + p.Sweep * (lo + hi) / 2));
                if (p.Center.X + cosineSign * p.Radius * Math.Sqrt(Math.Max(0, 1 - sine * sine)) > point.X) inside = !inside;
            }
        }
        return inside;
    }

    public static double Distance(CadPlanarPrimitive p, CadPointD point)
    {
        if (p.IsLine) return point.DistanceTo(p.At(Math.Clamp(p.Parameter(point), 0, 1)));
        var angle = Math.Atan2(point.Y - p.Center.Y, point.X - p.Center.X);
        return p.Contains(CadPlanarPrimitive.Point(p.Center, p.Radius, angle))
            ? Math.Abs(point.DistanceTo(p.Center) - p.Radius) : Math.Min(point.DistanceTo(p.Start), point.DistanceTo(p.End));
    }

    public static double Area(IReadOnlyList<CadRegionContour> contours)
    {
        double result = 0;
        foreach (var c in contours)
        {
            // Boundary points determine nesting without relying on the stored orientation.
            var point = c.Edges[0].At(.371);
            var depth = contours.Count(other => !ReferenceEquals(c, other) && Contains(other.Edges, point));
            result += (depth % 2 == 0 ? 1 : -1) * Math.Abs(c.SignedArea);
        }
        return Math.Abs(result);
    }
}
