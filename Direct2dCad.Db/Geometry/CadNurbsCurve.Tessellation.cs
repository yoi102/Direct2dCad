namespace Direct2dCad.Db.Geometry;

public sealed partial class CadNurbsCurve
{
    /// <summary>Positive rational Bezier hulls provide a conservative curve-to-chord bound.</summary>
    public CadCurveTessellationResult Tessellate(double tolerance = 1e-4, CancellationToken token = default)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var points = new List<CadPointD>(); var maximum = 0.0; var exhausted = false; var nodes = 0;
        foreach (var controls in BezierControls(token)) Subdivide(controls, 0);
        return new(points.AsReadOnly(), maximum, exhausted);

        CadPointD Project(HPoint h) => new(_points[0].X + h.X / h.W, _points[0].Y + h.Y / h.W);
        void Subdivide(HPoint[] controls, int depth)
        {
            token.ThrowIfCancellationRequested(); nodes++;
            var a = Project(controls[0]); var b = Project(controls[^1]); var delta = b - a;
            var bound = 0.0;
            foreach (var h in controls)
            {
                var point = Project(h);
                var t = delta.LengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(delta) / delta.LengthSquared, 0, 1);
                bound = Math.Max(bound, point.DistanceTo(a + delta * t));
            }
            if (bound <= tolerance || depth >= 24 || nodes >= 65536)
            {
                maximum = Math.Max(maximum, bound); exhausted |= bound > tolerance;
                if (points.Count == 0 || points[^1] != a) points.Add(a);
                points.Add(b); return;
            }
            var work = controls.ToArray(); var left = new HPoint[Degree + 1]; var right = new HPoint[Degree + 1];
            left[0] = work[0]; right[Degree] = work[Degree];
            for (var r = 1; r <= Degree; r++)
            {
                for (var j = 0; j <= Degree - r; j++) work[j] = work[j] * .5 + work[j + 1] * .5;
                left[r] = work[0]; right[Degree - r] = work[Degree - r];
            }
            Subdivide(left, depth + 1); Subdivide(right, depth + 1);
        }
    }

    // Extract each span independently through knot insertion, avoiding quadratic global array copies.
    private IEnumerable<HPoint[]> BezierControls(CancellationToken token)
    {
        for (var span = Degree; span < _points.Length; span++)
        {
            if (_knots[span] == _knots[span + 1]) continue;
            token.ThrowIfCancellationRequested();
            var a = _knots[span]; var b = _knots[span + 1];
            var controls = _homogeneous[(span - Degree)..(span + 1)].ToList();
            var knots = _knots[(span - Degree)..(span + Degree + 2)].ToList();
            Insert(a); Insert(b);
            var k = knots.FindIndex(Degree, v => v == b) - 1;
            yield return controls.Skip(k - Degree).Take(Degree + 1).ToArray();

            void Insert(double u)
            {
                while (knots.Count(k => k == u) < Degree)
                {
                    var n = controls.Count - 1;
                    var k = u == b ? n + 1 : knots.FindLastIndex(v => v <= u);
                    var s = knots.Count(v => v == u);
                    var next = new HPoint[controls.Count + 1];
                    for (var i = 0; i <= k - Degree; i++) next[i] = controls[i];
                    for (var i = k - s; i <= n; i++) next[i + 1] = controls[i];
                    for (var i = k - Degree + 1; i <= k - s; i++)
                    {
                        var alpha = (u - knots[i]) / (knots[i + Degree] - knots[i]);
                        next[i] = controls[i - 1] * (1 - alpha) + controls[i] * alpha;
                    }
                    controls = next.ToList(); knots.Insert(k + 1, u);
                }
            }
        }
    }
}
