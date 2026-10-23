namespace Direct2dCad.Db.Geometry;

/// <summary>Immutable, positive-weight planar NURBS. Parameters use the supplied knot domain.</summary>
public sealed partial class CadNurbsCurve
{
    private readonly CadPointD[] _points;
    private readonly double[] _weights;
    private readonly double[] _knots;
    private readonly HPoint[] _homogeneous;
    private readonly HPoint[] _derivatives;
    private readonly double[] _derivativeKnots;
    public int Degree { get; }
    public bool IsPeriodic { get; }
    public IReadOnlyList<CadPointD> ControlPoints { get; }
    public IReadOnlyList<double> Weights { get; }
    public IReadOnlyList<double> Knots { get; }
    public int EditablePointCount => _points.Length - (IsPeriodic ? Degree : 0);
    public double ParameterStart => _knots[Degree];
    public double ParameterEnd => _knots[_points.Length];
    /// <summary>Number of positive knot intervals in the active parameter domain.</summary>
    public int ParameterSpanCount { get; }
    public bool IsRational { get; }
    public CadRectD Bounds { get; }

    public CadNurbsCurve(int degree, IEnumerable<CadPointD> controlPoints, IEnumerable<double> knots,
        IEnumerable<double>? weights = null, bool periodic = false)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(knots);
        _points = controlPoints.ToArray(); _knots = knots.ToArray();
        _weights = weights?.ToArray() ?? Enumerable.Repeat(1.0, _points.Length).ToArray();
        if (degree is < 1 or > 16 || _points.Length <= degree || _points.Length > 100000)
            throw new ArgumentException("NURBS degree must be 1..16 and smaller than the control point count.");
        if (_points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) ||
            _weights.Length != _points.Length || _weights.Any(w => !double.IsFinite(w) || w <= 0))
            throw new ArgumentException("NURBS points must be finite and each point must have a positive finite weight.");
        if (_knots.Length != _points.Length + degree + 1 || _knots.Any(k => !double.IsFinite(k)) ||
            _knots.Zip(_knots.Skip(1)).Any(k => k.First > k.Second) ||
            !double.IsFinite(_knots[_points.Length] - _knots[degree]) || _knots[_points.Length] <= _knots[degree])
            throw new ArgumentException("Invalid NURBS knot vector or parameter domain.");
        Degree = degree; IsPeriodic = periodic;
        for (var i = degree; i < _points.Length; i++)
            if (_knots[i] < _knots[i + 1]) ParameterSpanCount++;
        IsRational = _weights.Any(w => w != _weights[0]);
        foreach (var group in _knots.GroupBy(k => k))
            if (group.Count() > degree + 1 || group.Key > ParameterStart && group.Key < ParameterEnd && group.Count() > degree)
                throw new ArgumentException("Discontinuous NURBS knots are not supported by a single CAD curve.");
        if (periodic)
        {
            if (EditablePointCount <= degree) throw new ArgumentException("A periodic NURBS needs more distinct controls than its degree.");
            for (var i = 0; i < degree; i++)
                if (_points[i] != _points[EditablePointCount + i] || _weights[i] != _weights[EditablePointCount + i])
                    throw new ArgumentException("Periodic control points and weights must wrap at the seam.");
            var period = ParameterEnd - ParameterStart;
            for (var i = 0; i <= 2 * degree; i++)
                if (Math.Abs((_knots[i + EditablePointCount] - _knots[i]) - period) > period * 1e-10)
                    throw new ArgumentException("Periodic knot intervals must wrap consistently at the seam.");
        }
        ControlPoints = Array.AsReadOnly(_points); Weights = Array.AsReadOnly(_weights); Knots = Array.AsReadOnly(_knots);
        var bounds = CadRectD.Empty;
        foreach (var p in _points) bounds = bounds.ExpandToInclude(p);
        Bounds = bounds;
        var scale = _weights.Max(); var origin = _points[0];
        _homogeneous = _points.Select((p, i) => new HPoint((p.X - origin.X) * (_weights[i] / scale),
            (p.Y - origin.Y) * (_weights[i] / scale), _weights[i] / scale)).ToArray();
        if (_homogeneous.Any(h => !double.IsFinite(h.X) || !double.IsFinite(h.Y) || h.W <= 0))
            throw new ArgumentException("NURBS coordinate or weight range exceeds numerical precision.");
        _derivatives = new HPoint[_points.Length - 1];
        for (var i = 0; i < _derivatives.Length; i++)
        {
            var denominator = _knots[i + degree + 1] - _knots[i + 1];
            _derivatives[i] = denominator == 0 ? default : (_homogeneous[i + 1] - _homogeneous[i]) * (degree / denominator);
        }
        _derivativeKnots = _knots[1..^1];
        if (_derivatives.Any(h => !double.IsFinite(h.X) || !double.IsFinite(h.Y) || !double.IsFinite(h.W)))
            throw new ArgumentException("NURBS knot spacing exceeds numerical precision.");
        if (periodic && Evaluate(ParameterStart).DistanceTo(Evaluate(ParameterEnd)) > Math.Max(1e-8, bounds.Width * 1e-10 + bounds.Height * 1e-10))
            throw new ArgumentException("The periodic NURBS knot vector does not close the seam.");
    }

    public CadPointD Evaluate(double parameter) => EvaluateInSpan(ValidateParameter(parameter), FindSpan(parameter)).Point;
    public CadVectorD Derivative(double parameter) => EvaluateInSpan(ValidateParameter(parameter), FindSpan(parameter)).Derivative;
    public CadPointD EvaluateNormalized(double parameter) => Evaluate(ParameterStart + parameter * (ParameterEnd - ParameterStart));
    private double ValidateParameter(double parameter)
    {
        if (!double.IsFinite(parameter) || parameter < ParameterStart || parameter > ParameterEnd)
            throw new ArgumentOutOfRangeException(nameof(parameter));
        return parameter;
    }
    private int FindSpan(double u)
    {
        if (u == ParameterEnd) return _points.Length - 1;
        var low = Degree; var high = _points.Length;
        while (high - low > 1) { var mid = (low + high) / 2; if (u < _knots[mid]) high = mid; else low = mid; }
        return low;
    }
    private (CadPointD Point, CadVectorD Derivative) EvaluateInSpan(double u, int span)
    {
        var h = DeBoor(Degree, _knots, _homogeneous, u, span);
        var d = DeBoor(Degree - 1, _derivativeKnots, _derivatives, u, span - 1);
        var x = h.X / h.W; var y = h.Y / h.W;
        return (new(_points[0].X + x, _points[0].Y + y), new((d.X - x * d.W) / h.W, (d.Y - y * d.W) / h.W));
    }
    private static HPoint DeBoor(int degree, double[] knots, HPoint[] controls, double u, int span)
    {
        Span<HPoint> d = stackalloc HPoint[17];
        for (var j = 0; j <= degree; j++) d[j] = controls[span - degree + j];
        for (var r = 1; r <= degree; r++)
            for (var j = degree; j >= r; j--)
            {
                var i = span - degree + j; var denominator = knots[i + degree - r + 1] - knots[i];
                var alpha = denominator == 0 ? 0 : (u - knots[i]) / denominator;
                d[j] = d[j - 1] * (1 - alpha) + d[j] * alpha;
            }
        return d[degree];
    }
    public IEnumerable<(double Start, double End)> ParameterSpans()
    {
        for (var i = Degree; i < _points.Length; i++)
            if (_knots[i + 1] > _knots[i]) yield return (_knots[i], _knots[i + 1]);
    }

    /// <summary>Cubic display approximation only. NURBS data remains authoritative for editing and exchange.</summary>
    public IReadOnlyList<CadBezierSegmentD> CreateDisplayBeziers(double tolerance = 1e-6)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var result = new List<CadBezierSegmentD>();
        foreach (var controls in BezierControls(default)) Add(controls, 0);
        return result.AsReadOnly();
        void Add(HPoint[] controls, int depth)
        {
            if (result.Count >= 262144) throw new InvalidOperationException("NURBS display approximation exceeded its budget.");
            var a = controls[0]; var b = controls[^1];
            var start = new CadPointD(a.X / a.W, a.Y / a.W); var end = new CadPointD(b.X / b.W, b.Y / b.W);
            var da = (controls[1] - a) * Degree; var db = (b - controls[^2]) * Degree;
            var first = start + new CadVectorD((da.X - start.X * da.W) / a.W, (da.Y - start.Y * da.W) / a.W) / 3;
            var second = end - new CadVectorD((db.X - end.X * db.W) / b.W, (db.Y - end.Y * db.W) / b.W) / 3;
            CadPointD[] cubic = [start, first, second, end];
            var exact = Degree <= 3 && !IsRational;
            // Bernstein coefficients of numerator - denominator * cubic bound the error
            // everywhere on the interval, including features missed by fixed samples.
            var error = 0.0;
            if (!exact)
                for (var k = 0; k <= Degree + 3; k++)
                {
                    var x = 0.0; var y = 0.0;
                    for (var i = Math.Max(0, k - 3); i <= Math.Min(Degree, k); i++)
                    {
                        var j = k - i; var factor = Choose(Degree, i) * Choose(3, j) / Choose(Degree + 3, k);
                        x += factor * (controls[i].X - controls[i].W * cubic[j].X);
                        y += factor * (controls[i].Y - controls[i].W * cubic[j].Y);
                    }
                    error = Math.Max(error, Math.Sqrt(x * x + y * y) / controls.Min(h => h.W));
                }
            if (error <= tolerance)
            {
                CadPointD Global(CadPointD p) => new(p.X + _points[0].X, p.Y + _points[0].Y);
                result.Add(new(Global(start), Global(first), Global(second), Global(end))); return;
            }
            if (depth >= 24) throw new InvalidOperationException("NURBS display approximation exceeded its budget.");
            var work = controls.ToArray(); var left = new HPoint[Degree + 1]; var right = new HPoint[Degree + 1];
            left[0] = work[0]; right[Degree] = work[Degree];
            for (var r = 1; r <= Degree; r++)
            {
                for (var j = 0; j <= Degree - r; j++) work[j] = work[j] * .5 + work[j + 1] * .5;
                left[r] = work[0]; right[Degree - r] = work[Degree - r];
            }
            Add(left, depth + 1); Add(right, depth + 1);
        }
        static double Choose(int n, int k) { var value = 1.0; for (var i = 1; i <= k; i++) value *= (double)(n - i + 1) / i; return value; }
    }

    public CadNurbsCurve Transform(Func<CadPointD, CadPointD> transform) => new(Degree, _points.Select(transform), _knots, _weights, IsPeriodic);
    public CadNurbsCurve WithControlPoint(int index, CadPointD point, double weight)
    {
        if (index < 0 || index >= EditablePointCount) throw new ArgumentOutOfRangeException(nameof(index));
        var points = _points.ToArray(); var weights = _weights.ToArray(); points[index] = point; weights[index] = weight;
        if (IsPeriodic && index < Degree) { points[EditablePointCount + index] = point; weights[EditablePointCount + index] = weight; }
        return new(Degree, points, _knots, weights, IsPeriodic);
    }
    public static CadNurbsCurve CreateUniform(IEnumerable<CadPointD> controls, int degree = 3, IEnumerable<double>? weights = null, bool closed = false)
    {
        var points = controls.ToArray(); var w = weights?.ToArray() ?? Enumerable.Repeat(1.0, points.Length).ToArray();
        if (degree < 1 || points.Length <= degree || w.Length != points.Length) throw new ArgumentException("Insufficient NURBS controls for the requested degree.");
        if (closed)
        {
            var count = points.Length;
            return new(degree, points.Concat(points.Take(degree)),
                Enumerable.Range(0, count + 2 * degree + 1).Select(i => (double)(i - degree) / count), w.Concat(w.Take(degree)), true);
        }
        var knots = new double[points.Length + degree + 1];
        for (var i = 0; i < knots.Length; i++) knots[i] = i <= degree ? 0 : i >= points.Length ? 1 : (double)(i - degree) / (points.Length - degree);
        return new(degree, points, knots, w);
    }
    private readonly record struct HPoint(double X, double Y, double W)
    {
        public static HPoint operator +(HPoint a, HPoint b) => new(a.X + b.X, a.Y + b.Y, a.W + b.W);
        public static HPoint operator -(HPoint a, HPoint b) => new(a.X - b.X, a.Y - b.Y, a.W - b.W);
        public static HPoint operator *(HPoint a, double b) => new(a.X * b, a.Y * b, a.W * b);
    }
}
