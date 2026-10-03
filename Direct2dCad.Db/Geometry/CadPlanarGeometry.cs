namespace Direct2dCad.Db.Geometry;

/// <summary>All distances use document millimetres; screen tolerance is never used for geometry editing.</summary>
public static class CadGeometryTolerance
{
    public const double Absolute = 1e-8;
    public static double For(double scale) => Math.Max(Absolute, Math.Abs(scale) * 1e-12);
    public static bool Coincident(CadPointD a, CadPointD b) => a.DistanceTo(b) <= For(Math.Max(Math.Max(Math.Abs(a.X), Math.Abs(a.Y)), Math.Max(Math.Abs(b.X), Math.Abs(b.Y))));
}

public readonly record struct CadPlanarPrimitive(CadPointD Start, CadPointD End, CadPointD Center, double Radius, double StartAngle, double Sweep)
{
    public bool IsLine => Radius == 0;
    public static CadPlanarPrimitive Line(CadPointD a, CadPointD b) => new(a, b, default, 0, 0, 0);
    public static CadPlanarPrimitive Arc(CadPointD center, double radius, double start, double sweep) =>
        new(Point(center, radius, start), Point(center, radius, start + sweep), center, radius, start, sweep);
    public static CadPointD Point(CadPointD c, double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    public CadPointD At(double t) => IsLine ? new(Start.X + (End.X - Start.X) * t, Start.Y + (End.Y - Start.Y) * t) : Point(Center, Radius, StartAngle + Sweep * t);
    public double Parameter(CadPointD p)
    {
        if (IsLine)
        {
            var dx = End.X - Start.X; var dy = End.Y - Start.Y;
            var d = dx * dx + dy * dy;
            return d <= CadGeometryTolerance.Absolute * CadGeometryTolerance.Absolute ? 0 : ((p.X - Start.X) * dx + (p.Y - Start.Y) * dy) / d;
        }
        var a = Math.Atan2(p.Y - Center.Y, p.X - Center.X) - StartAngle;
        return PositiveAngle(Sweep > 0 ? a : -a) / Math.Abs(Sweep);
    }
    public bool Contains(CadPointD p)
    {
        var t = Parameter(p);
        return t >= -1e-10 && t <= 1 + 1e-10;
    }
    public static double PositiveAngle(double a) => (a % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
}

public static class CadPlanarGeometry
{
    public static double Cross(CadVectorD a, CadVectorD b) => a.X * b.Y - a.Y * b.X;
    public static IReadOnlyList<CadPointD> Intersections(CadPlanarPrimitive a, CadPlanarPrimitive b, bool extendA = false, bool extendB = false)
    {
        var result = new List<CadPointD>(2);
        void Add(CadPointD p)
        {
            if ((!extendA && !a.Contains(p)) || (!extendB && !b.Contains(p)) || result.Any(q => CadGeometryTolerance.Coincident(p, q))) return;
            result.Add(p);
        }
        if (a.IsLine && b.IsLine)
        {
            var u = a.End - a.Start; var v = b.End - b.Start;
            var den = Cross(u, v);
            if (Math.Abs(den) <= 1e-12 * Math.Sqrt(u.LengthSquared*v.LengthSquared)) return result;
            Add(a.At(Cross(b.Start - a.Start, v) / den));
        }
        else if (a.IsLine || b.IsLine)
        {
            var line = a.IsLine ? a : b; var arc = a.IsLine ? b : a;
            var u = line.End - line.Start; var v = line.Start - arc.Center;
            var aa = u.X * u.X + u.Y * u.Y;
            if (aa <= 1e-20) return result;
            var closest=-u.Dot(v)/aa;
            var distance=line.At(closest).DistanceTo(arc.Center);
            var discriminant=arc.Radius*arc.Radius-distance*distance;
            var tolerance=2*CadGeometryTolerance.For(Math.Max(arc.Radius,distance))*Math.Max(arc.Radius,distance);
            if(discriminant < -tolerance) return result;
            var delta=Math.Sqrt(Math.Max(0,discriminant)/aa);
            Add(line.At(closest-delta));Add(line.At(closest+delta));
        }
        else
        {
            var d = a.Center.DistanceTo(b.Center); var tolerance = CadGeometryTolerance.For(Math.Max(a.Radius, b.Radius));
            if (d <= tolerance || d > a.Radius + b.Radius + tolerance || d < Math.Abs(a.Radius - b.Radius) - tolerance) return result;
            var x = (a.Radius * a.Radius - b.Radius * b.Radius + d * d) / (2 * d);
            var h = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - x * x));
            var ux = (b.Center.X - a.Center.X) / d; var uy = (b.Center.Y - a.Center.Y) / d;
            Add(new(a.Center.X + x * ux - h * uy, a.Center.Y + x * uy + h * ux));
            Add(new(a.Center.X + x * ux + h * uy, a.Center.Y + x * uy - h * ux));
        }
        return result;
    }
}
