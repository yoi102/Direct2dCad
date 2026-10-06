namespace Direct2dCad.Db.Geometry;

/// <summary>
/// Analytic conic intersections. The boundary remains an ellipse; only the roots of its
/// implicit intersection equation are evaluated numerically. No polygon determines topology.
/// </summary>
internal static class CadConicIntersections
{
    private const double MachineEpsilon = 2.2204460492503131e-16;

    internal static bool SameSupport(CadPlanarPrimitive a, CadPlanarPrimitive b)
    {
        if (a.IsLine != b.IsLine) return false;
        if (a.IsLine)
        {
            var u = (a.End - a.Start).Normalize(); var v = (b.End - b.Start).Normalize();
            var scale = Math.Max(1, Math.Max(Math.Max(Math.Abs(a.Start.X), Math.Abs(a.Start.Y)),
                Math.Max(Math.Abs(b.Start.X), Math.Abs(b.Start.Y))));
            return Math.Abs(u.Cross(v)) <= 64 * MachineEpsilon && Math.Abs(u.Cross(b.Start - a.Start)) <= scale * 64 * MachineEpsilon;
        }
        var rotation = a.Rotation - b.Rotation; var cr = Math.Cos(rotation); var sr = Math.Sin(rotation);
        var u1 = new CadVectorD(a.RadiusX * cr / b.RadiusX, a.RadiusX * sr / b.RadiusY);
        var v1 = new CadVectorD(-a.RadiusY * sr / b.RadiusX, a.RadiusY * cr / b.RadiusY);
        var w = Local(b, a.Center); var aa = u1.Dot(u1); var cc = v1.Dot(v1);
        var scale1 = Math.Max(1, Math.Max(aa + cc, w.LengthSquared));
        return Math.Abs((aa - cc) / 2) <= 64 * MachineEpsilon * scale1 && Math.Abs(u1.Dot(v1)) <= 64 * MachineEpsilon * scale1 &&
            Math.Abs(u1.Dot(w)) <= 32 * MachineEpsilon * scale1 && Math.Abs(v1.Dot(w)) <= 32 * MachineEpsilon * scale1 &&
            Math.Abs((aa + cc) / 2 + w.LengthSquared - 1) <= 64 * MachineEpsilon * scale1;
    }

    public static IReadOnlyList<CadPointD> Intersections(CadPlanarPrimitive a, CadPlanarPrimitive b, bool extendA, bool extendB)
    {
        ValidateCondition(a); ValidateCondition(b);
        var result = new List<CadPointD>(4);
        void Add(CadPointD point)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidOperationException("Conic intersection is outside the finite coordinate range.");
            if ((!extendA && !a.Contains(point)) || (!extendB && !b.Contains(point))) return;
            if (!result.Any(p => CadGeometryTolerance.Coincident(p, point))) result.Add(point);
        }
        if (a.IsLine || b.IsLine)
        {
            var line = a.IsLine ? a : b; var ellipse = a.IsLine ? b : a;
            var start = Local(ellipse, line.Start); var end = Local(ellipse, line.End);
            var direction = end - start; var aa = direction.LengthSquared;
            if (aa <= double.Epsilon) return result;
            var parameter = -start.Dot(direction) / aa;
            var closest = start + direction * parameter;
            var h2 = 1 - closest.LengthSquared;
            var error = 64 * MachineEpsilon * Math.Max(1, closest.LengthSquared);
            if (h2 < -error) return result;
            var delta = Math.Sqrt(Math.Max(0, h2) / aa);
            Add(line.At(parameter - delta)); Add(line.At(parameter + delta));
            return result;
        }

        // Express A(theta) in B's normalized coordinates; B is the unit circle there.
        var rotation = a.Rotation - b.Rotation;
        var cr = Math.Cos(rotation); var sr = Math.Sin(rotation);
        var u = new CadVectorD(a.RadiusX * cr / b.RadiusX, a.RadiusX * sr / b.RadiusY);
        var v = new CadVectorD(-a.RadiusY * sr / b.RadiusX, a.RadiusY * cr / b.RadiusY);
        var w = Local(b, a.Center);
        var ac = u.Dot(u); var bc = 2 * u.Dot(v); var cc = v.Dot(v);
        var dc = 2 * u.Dot(w); var ec = 2 * v.Dot(w); var fc = w.Dot(w) - 1;
        // cos² + sin² = 1. Test the five independent Fourier coefficients.
        var residual = Math.Max(Math.Max(Math.Abs((ac - cc) / 2), Math.Abs(bc / 2)),
            Math.Max(Math.Max(Math.Abs(dc), Math.Abs(ec)), Math.Abs((ac + cc) / 2 + fc)));
        var scale = Math.Max(1, Math.Max(ac + cc, Math.Abs(fc)));
        if (residual <= 64 * MachineEpsilon * scale) return result; // Coincident supports; caller nodes arc endpoints.
        var geometricTolerance = CadGeometryTolerance.For(Math.Max(
            Math.Max(Math.Max(Math.Abs(a.Center.X), Math.Abs(a.Center.Y)), Math.Max(Math.Abs(b.Center.X), Math.Abs(b.Center.Y))),
            Math.Max(Math.Max(a.RadiusX, a.RadiusY), Math.Max(b.RadiusX, b.RadiusY))));
        if (residual * Math.Min(b.RadiusX, b.RadiusY) < geometricTolerance * 8)
            throw new InvalidOperationException("Nearly coincident elliptical boundaries cannot be resolved within geometry tolerance.");
        foreach (var angle in TrigonometricRoots(ac, bc, cc, dc, ec, fc, rejectAmbiguous: true))
        {
            var point = Point(a, angle);
            var q = Local(b, point);
            var normalLength = Math.Sqrt(q.X * q.X / (b.RadiusX * b.RadiusX) + q.Y * q.Y / (b.RadiusY * b.RadiusY));
            var distanceEstimate = Math.Abs(q.LengthSquared - 1) / Math.Max(2 * normalLength, double.Epsilon);
            if (distanceEstimate > geometricTolerance * 8)
                throw new InvalidOperationException("Elliptical intersection residual exceeds geometry tolerance.");
            Add(point);
        }
        if (result.Count > 4) throw new InvalidOperationException("Elliptical intersection roots could not be resolved unambiguously.");
        return result;
    }

    internal static void ValidateCondition(CadPlanarPrimitive p)
    {
        if (p.IsLine) return;
        var min = Math.Min(p.RadiusX, p.RadiusY); var max = Math.Max(p.RadiusX, p.RadiusY);
        var coordinate = Math.Max(Math.Abs(p.Center.X), Math.Abs(p.Center.Y));
        if (!double.IsFinite(min) || !double.IsFinite(max) || min <= CadGeometryTolerance.Absolute ||
            max / min > 1e8 || coordinate * MachineEpsilon * 64 >= min)
            throw new InvalidOperationException("Elliptical boundary is too small, eccentric, or distant from the origin for reliable Boolean geometry.");
    }

    internal static CadPointD Point(CadPlanarPrimitive p, double angle)
    {
        var x = p.RadiusX * Math.Cos(angle); var y = p.RadiusY * Math.Sin(angle);
        var c = Math.Cos(p.Rotation); var s = Math.Sin(p.Rotation);
        return new(p.Center.X + x * c - y * s, p.Center.Y + x * s + y * c);
    }

    private static CadVectorD Local(CadPlanarPrimitive p, CadPointD point)
    {
        var d = point - p.Center; var c = Math.Cos(p.Rotation); var s = Math.Sin(p.Rotation);
        return new((d.X * c + d.Y * s) / p.RadiusX, (-d.X * s + d.Y * c) / p.RadiusY);
    }

    /// <summary>All real roots of a cos²θ + b cosθ sinθ + c sin²θ + d cosθ + e sinθ + f.</summary>
    internal static IReadOnlyList<double> TrigonometricRoots(double a, double b, double c, double d, double e, double f, bool rejectAmbiguous = false)
    {
        if (new[] { a, b, c, d, e, f }.Any(x => !double.IsFinite(x)))
            throw new InvalidOperationException("Conic coefficients are outside the finite range.");
        var result = new List<double>(4);
        // Two overlapping half-angle charts keep t bounded in [-1,1]. A single chart
        // loses theta=pi and makes roots near it arbitrarily ill-conditioned.
        foreach (var sign in new[] { 1d, -1d })
        {
            var dd = d * sign; var ee = e * sign;
            double[] polynomial = [a + dd + f, 2 * (b + ee), -2 * a + 4 * c + 2 * f, 2 * (ee - b), a - dd + f];
            foreach (var t in RealRoots(polynomial, -1, 1, rejectAmbiguous))
            {
                var angle = CadPlanarPrimitive.PositiveAngle(2 * Math.Atan(t) + (sign < 0 ? Math.PI : 0));
                if (!result.Any(x => Math.Abs(Math.IEEERemainder(x - angle, Math.PI * 2)) <= 2e-12)) result.Add(angle);
            }
        }
        return result;
    }

    private static IReadOnlyList<double> RealRoots(double[] coefficients, double min, double max, bool rejectAmbiguous)
    {
        var magnitude = coefficients.Max(Math.Abs);
        if (magnitude == 0) return [0];
        var c = coefficients.Select(x => x / magnitude).ToArray();
        var degree = c.Length - 1;
        while (degree > 0 && Math.Abs(c[degree]) <= 4 * MachineEpsilon) degree--;
        if (degree == 0) return [];
        if (degree == 1)
        {
            var root = -c[0] / c[1];
            return root >= min - 2e-14 && root <= max + 2e-14 ? [Math.Clamp(root, min, max)] : [];
        }
        var derivative = Enumerable.Range(1, degree).Select(i => i * c[i]).ToArray();
        var critical = RealRoots(derivative, min, max, rejectAmbiguous);
        var stops = new[] { min }.Concat(critical).Append(max).Distinct().Order().ToArray();
        var roots = new List<double>(degree);
        double Evaluate(double x)
        {
            var value = c[degree];
            for (var i = degree - 1; i >= 0; i--) value = Math.FusedMultiplyAdd(value, x, c[i]);
            return value;
        }
        void Add(double x) { if (!roots.Any(r => Math.Abs(r - x) <= 2e-13)) roots.Add(x); }
        // Stationary roots include even multiplicities (tangent conics), which sign-only
        // root searches miss. The threshold bounds evaluation roundoff on the unit interval.
        var error = c.Take(degree + 1).Sum(Math.Abs) * 16 * MachineEpsilon;
        foreach (var x in stops)
        {
            var residual = Math.Abs(Evaluate(x));
            if (residual > error) continue;
            if (rejectAmbiguous && x > min + 1e-13 && x < max - 1e-13 && residual > error / 8)
                throw new InvalidOperationException("A nearly tangent conic root cannot be separated reliably at double precision.");
            Add(x);
        }
        for (var i = 1; i < stops.Length; i++)
        {
            var lo = stops[i - 1]; var hi = stops[i]; var fl = Evaluate(lo); var fh = Evaluate(hi);
            if (Math.Abs(fl) <= error || Math.Abs(fh) <= error || Math.Sign(fl) == Math.Sign(fh)) continue;
            for (var iteration = 0; iteration < 64 && hi - lo > 2e-15; iteration++)
            {
                var mid = (lo + hi) / 2; var fm = Evaluate(mid);
                if (fm == 0) { lo = hi = mid; break; }
                if (Math.Sign(fm) == Math.Sign(fl)) { lo = mid; fl = fm; } else hi = mid;
            }
            Add((lo + hi) / 2);
        }
        return roots;
    }
}
