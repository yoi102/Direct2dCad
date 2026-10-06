namespace Direct2dCad.Db.Geometry;

/// <summary>All distances use document millimetres; screen tolerance is never used for geometry editing.</summary>
public static class CadGeometryTolerance
{
    public const double Absolute = 1e-8;
    public static double For(double scale) => Math.Max(Absolute, Math.Abs(scale) * 1e-12);
    public static bool Coincident(CadPointD a, CadPointD b) => a.DistanceTo(b) <= For(Math.Max(Math.Max(Math.Abs(a.X), Math.Abs(a.Y)), Math.Max(Math.Abs(b.X), Math.Abs(b.Y))));
}

public readonly record struct CadPlanarPrimitive(CadPointD Start, CadPointD End, CadPointD Center, double Radius, double StartAngle, double Sweep,
    double EllipseRadiusY = 0, double EllipseRotation = 0)
{
    public bool IsLine => Radius == 0;
    public bool IsEllipse => EllipseRadiusY > 0;
    public double RadiusX => Radius;
    public double RadiusY => IsEllipse ? EllipseRadiusY : Radius;
    public double Rotation => EllipseRotation;
    public static CadPlanarPrimitive Line(CadPointD a, CadPointD b) => new(a, b, default, 0, 0, 0);
    public static CadPlanarPrimitive Arc(CadPointD center, double radius, double start, double sweep) =>
        new(Point(center, radius, start), Point(center, radius, start + sweep), center, radius, start, sweep);
    public static CadPlanarPrimitive EllipseArc(CadPointD center, double rx, double ry, double rotation, double start, double sweep)
    {
        var primitive = new CadPlanarPrimitive(default, default, center, rx, start, sweep, ry, rotation);
        return primitive with { Start = primitive.At(0), End = primitive.At(1) };
    }
    public static CadPointD Point(CadPointD c, double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    public CadPointD At(double t)
    {
        if (IsLine) return new(Start.X + (End.X - Start.X) * t, Start.Y + (End.Y - Start.Y) * t);
        var angle = StartAngle + Sweep * t;
        var x = RadiusX * Math.Cos(angle); var y = RadiusY * Math.Sin(angle);
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        return new(Center.X + c * x - s * y, Center.Y + s * x + c * y);
    }
    public CadVectorD TangentAt(double t)
    {
        if (IsLine) return End - Start;
        var angle = StartAngle + Sweep * t;
        var x = -RadiusX * Math.Sin(angle) * Sweep; var y = RadiusY * Math.Cos(angle) * Sweep;
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        return new(c * x - s * y, s * x + c * y);
    }
    public CadPlanarPrimitive Slice(double a, double b) => IsLine ? Line(At(a), At(b)) : IsEllipse
        ? EllipseArc(Center, RadiusX, RadiusY, Rotation, StartAngle + Sweep * a, Sweep * (b - a))
        : Arc(Center, Radius, StartAngle + Sweep * a, Sweep * (b - a));
    public CadPlanarPrimitive Reversed() => Slice(1, 0);
    public double Length => IsLine ? Start.DistanceTo(End) : IsEllipse
        ? CadCurveMeasurements.EllipseLength(RadiusX, RadiusY, StartAngle, Sweep, CadCurveMeasurements.DefaultError, default).Value
        : Radius * Math.Abs(Sweep);
    public IEnumerable<double> ExtremaParameters()
    {
        if (IsLine) yield break;
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        var x = Math.Atan2(-RadiusY * s, RadiusX * c);
        var y = Math.Atan2(RadiusY * c, RadiusX * s);
        foreach (var angle in new[] { x, x + Math.PI, y, y + Math.PI })
        {
            var t = AngleParameter(angle);
            if (t > 0 && t < 1) yield return t;
        }
    }
    public CadRectD Bounds
    {
        get
        {
            var bounds = CadRectD.Empty.ExpandToInclude(Start).ExpandToInclude(End);
            foreach (var t in ExtremaParameters()) bounds = bounds.ExpandToInclude(At(t));
            return bounds;
        }
    }
    public CadPointD NearestPoint(CadPointD point)
    {
        if (IsLine) return At(Math.Clamp(Parameter(point), 0, 1));
        if (!IsEllipse)
        {
            var direction = point - Center;
            if (direction.LengthSquared == 0) return Start;
            var projected = Center + direction.Normalize() * Radius;
            return Contains(projected) ? projected : Start.DistanceTo(point) <= End.DistanceTo(point) ? Start : End;
        }
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        var dx = point.X - Center.X; var dy = point.Y - Center.Y;
        var x = c * dx + s * dy; var y = -s * dx + c * dy;
        var best = Start; var distance = best.DistanceTo(point);
        if (End.DistanceTo(point) < distance) { best = End; distance = best.DistanceTo(point); }
        foreach (var angle in CadConicIntersections.TrigonometricRoots(0, RadiusY * RadiusY - RadiusX * RadiusX, 0, -RadiusY * y, RadiusX * x, 0))
        {
            var t = AngleParameter(angle);
            if (t < -1e-10 || t > 1 + 1e-10) continue;
            var candidate = At(Math.Clamp(t, 0, 1)); var candidateDistance = candidate.DistanceTo(point);
            if (candidateDistance < distance) { best = candidate; distance = candidateDistance; }
        }
        return best;
    }
    public double Parameter(CadPointD p)
    {
        if (IsLine)
        {
            var lineDx = End.X - Start.X; var lineDy = End.Y - Start.Y;
            var d = lineDx * lineDx + lineDy * lineDy;
            return d <= CadGeometryTolerance.Absolute * CadGeometryTolerance.Absolute ? 0 : ((p.X - Start.X) * lineDx + (p.Y - Start.Y) * lineDy) / d;
        }
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        var dx = p.X - Center.X; var dy = p.Y - Center.Y;
        return AngleParameter(Math.Atan2((-s * dx + c * dy) / RadiusY, (c * dx + s * dy) / RadiusX));
    }
    private double AngleParameter(double angle) => PositiveAngle(Sweep > 0 ? angle - StartAngle : StartAngle - angle) / Math.Abs(Sweep);
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
        if (a.IsEllipse || b.IsEllipse) return CadConicIntersections.Intersections(a, b, extendA, extendB);
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
