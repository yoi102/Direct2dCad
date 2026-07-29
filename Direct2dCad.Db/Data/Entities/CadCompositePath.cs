using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public abstract record CadCompositePathSegment;

public sealed record CadCompositeLineSegment(CadPointD End) : CadCompositePathSegment;

public sealed record CadCompositeArcSegment : CadCompositePathSegment
{
    public CadPointD Center { get; }
    public double SweepAngleRadians { get; }

    public CadCompositeArcSegment(CadPointD center, double sweepAngleRadians)
    {
        Center = center;
        SweepAngleRadians = CadCompositePath.GuardSweep(sweepAngleRadians);
    }
}

public sealed record CadCompositeSplineSegment : CadCompositePathSegment
{
    private readonly CadPointD[] _fitPoints;

    /// <summary>Fit points after the previous segment endpoint. The final item is this segment endpoint.</summary>
    public IReadOnlyList<CadPointD> FitPoints => _fitPoints;

    public CadCompositeSplineSegment(IEnumerable<CadPointD> fitPoints)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);
        _fitPoints = fitPoints.ToArray();
        if (_fitPoints.Length == 0)
            throw new ArgumentException("A composite spline segment requires at least one fit point.", nameof(fitPoints));
        foreach (var point in _fitPoints)
            CadCompositePath.GuardPoint(point, nameof(fitPoints));
    }
}

/// <summary>
/// One cubic Bezier segment. The segment start is the previous path endpoint.
/// </summary>
public sealed record CadCompositeBezierSegment : CadCompositePathSegment
{
    public CadPointD Control1 { get; }
    public CadPointD Control2 { get; }
    public CadPointD End { get; }

    public CadCompositeBezierSegment(CadPointD control1, CadPointD control2, CadPointD end)
    {
        CadCompositePath.GuardPoint(control1, nameof(control1));
        CadCompositePath.GuardPoint(control2, nameof(control2));
        CadCompositePath.GuardPoint(end, nameof(end));
        Control1 = control1;
        Control2 = control2;
        End = end;
    }
}

/// <summary>
/// One continuous path whose segments may mix lines, circular arcs, cubic Beziers,
/// and interpolating splines.
/// Segment start points are inferred from the path start and the preceding segment endpoint.
/// </summary>
public sealed class CadCompositePath : Curve
{
    private const int DefaultArcStepsPerCircle = 64;
    private const int DefaultSplineStepsPerBezier = 12;
    private readonly List<CadCompositePathSegment> _segments = [];
    private CadRectD _bounds = CadRectD.Empty;
    private double _length;

    public CadPointD StartPoint { get; private set; }
    public IReadOnlyList<CadCompositePathSegment> Segments => _segments;
    public bool Closed { get; private set; }
    public override bool IsClosed => Closed;
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? FillStyleId { get; private set; }
    public override double Length => _length;
    public override CadRectD Bounds => _bounds;

    internal CadCompositePath(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD startPoint,
        IEnumerable<CadCompositePathSegment> segments,
        bool closed = false,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        ReplaceGeometry(startPoint, segments, closed);
    }

    public void ReplaceGeometry(
        CadPointD startPoint,
        IEnumerable<CadCompositePathSegment> segments,
        bool closed)
    {
        GuardPoint(startPoint, nameof(startPoint));
        ArgumentNullException.ThrowIfNull(segments);
        var segmentArray = segments.ToArray();
        if (segmentArray.Length == 0)
            throw new ArgumentException("A composite path requires at least one segment.", nameof(segments));

        ValidateSegments(startPoint, segmentArray);
        StartPoint = startPoint;
        _segments.Clear();
        _segments.AddRange(segmentArray);
        Closed = closed;
        RebuildDerivedGeometry();
    }

    public CadPointD EndPoint
    {
        get
        {
            var current = StartPoint;
            foreach (var segment in _segments)
                current = GetEndPoint(current, segment);
            return current;
        }
    }

    public IEnumerable<CadPointD> EnumerateFlattenedPoints(
        int arcStepsPerCircle = DefaultArcStepsPerCircle,
        int splineStepsPerBezier = DefaultSplineStepsPerBezier,
        bool includeClosingPoint = true)
    {
        arcStepsPerCircle = Math.Max(4, arcStepsPerCircle);
        splineStepsPerBezier = Math.Max(1, splineStepsPerBezier);
        var current = StartPoint;
        yield return current;

        foreach (var segment in _segments)
        {
            switch (segment)
            {
                case CadCompositeLineSegment line:
                    current = line.End;
                    yield return current;
                    break;
                case CadCompositeArcSegment arc:
                    var vectorX = current.X - arc.Center.X;
                    var vectorY = current.Y - arc.Center.Y;
                    var radius = Math.Sqrt(vectorX * vectorX + vectorY * vectorY);
                    var startAngle = Math.Atan2(vectorY, vectorX);
                    var steps = Math.Max(1, (int)Math.Ceiling(
                        Math.Abs(arc.SweepAngleRadians) / (Math.PI * 2.0) * arcStepsPerCircle));
                    for (var step = 1; step <= steps; step++)
                    {
                        var angle = startAngle + arc.SweepAngleRadians * step / steps;
                        current = new CadPointD(
                            arc.Center.X + Math.Cos(angle) * radius,
                            arc.Center.Y + Math.Sin(angle) * radius);
                        yield return current;
                    }
                    break;
                case CadCompositeSplineSegment spline:
                    var points = new CadPointD[spline.FitPoints.Count + 1];
                    points[0] = current;
                    for (var index = 0; index < spline.FitPoints.Count; index++)
                        points[index + 1] = spline.FitPoints[index];
                    foreach (var bezier in CadSpline.CreateBezierSegments(points))
                    {
                        for (var step = 1; step <= splineStepsPerBezier; step++)
                            yield return bezier.Evaluate((double)step / splineStepsPerBezier);
                    }
                    current = spline.FitPoints[^1];
                    break;
                case CadCompositeBezierSegment bezier:
                    var cubic = new CadBezierSegmentD(
                        current,
                        bezier.Control1,
                        bezier.Control2,
                        bezier.End);
                    for (var step = 1; step <= splineStepsPerBezier; step++)
                        yield return cubic.Evaluate((double)step / splineStepsPerBezier);
                    current = bezier.End;
                    break;
            }
        }

        if (Closed && includeClosingPoint && !EndPoint.Equals(StartPoint))
            yield return StartPoint;
    }

    public static CadPointD GetEndPoint(CadPointD start, CadCompositePathSegment segment) => segment switch
    {
        CadCompositeLineSegment line => line.End,
        CadCompositeSplineSegment spline => spline.FitPoints[^1],
        CadCompositeArcSegment arc => RotateAround(start, arc.Center, arc.SweepAngleRadians),
        CadCompositeBezierSegment bezier => bezier.End,
        _ => throw new NotSupportedException($"Unsupported composite path segment: {segment.GetType().Name}")
    };

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;
    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;

    internal static double GuardSweep(double sweep)
    {
        if (!double.IsFinite(sweep) || Math.Abs(sweep) <= 1e-12 || Math.Abs(sweep) > Math.PI * 2.0 + 1e-12)
            throw new ArgumentOutOfRangeException(nameof(sweep), "Arc sweep must be non-zero and no greater than one full circle.");
        return sweep;
    }

    internal static void GuardPoint(CadPointD point, string parameterName)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(parameterName, "Point coordinates must be finite.");
    }

    private static void ValidateSegments(
        CadPointD startPoint,
        IReadOnlyList<CadCompositePathSegment> segments)
    {
        var current = startPoint;
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case CadCompositeLineSegment line:
                    GuardPoint(line.End, nameof(segments));
                    break;
                case CadCompositeArcSegment arc:
                    GuardPoint(arc.Center, nameof(segments));
                    if (current.DistanceSquaredTo(arc.Center) <= 1e-24)
                        throw new ArgumentException("An arc segment start point cannot equal its center.", nameof(segments));
                    break;
                case CadCompositeSplineSegment:
                    break;
                case CadCompositeBezierSegment:
                    break;
                case null:
                    throw new ArgumentException("Composite path segments cannot contain null.", nameof(segments));
                default:
                    throw new NotSupportedException($"Unsupported composite path segment: {segment.GetType().Name}");
            }
            current = GetEndPoint(current, segment);
        }
    }

    private void RebuildDerivedGeometry()
    {
        var bounds = CadRectD.Empty.ExpandToInclude(StartPoint);
        var current = StartPoint;
        foreach (var segment in _segments)
        {
            switch (segment)
            {
                case CadCompositeLineSegment line:
                    bounds = bounds.ExpandToInclude(line.End);
                    current = line.End;
                    break;
                case CadCompositeArcSegment arc:
                    var radius = current.DistanceTo(arc.Center);
                    var startAngle = Math.Atan2(current.Y - arc.Center.Y, current.X - arc.Center.X);
                    var end = GetEndPoint(current, arc);
                    bounds = bounds.ExpandToInclude(end);
                    foreach (var angle in new[] { 0.0, Math.PI * 0.5, Math.PI, Math.PI * 1.5 })
                    {
                        if (ContainsArcAngle(startAngle, arc.SweepAngleRadians, angle))
                        {
                            bounds = bounds.ExpandToInclude(new CadPointD(
                                arc.Center.X + Math.Cos(angle) * radius,
                                arc.Center.Y + Math.Sin(angle) * radius));
                        }
                    }
                    current = end;
                    break;
                case CadCompositeSplineSegment spline:
                    var points = new CadPointD[spline.FitPoints.Count + 1];
                    points[0] = current;
                    for (var index = 0; index < spline.FitPoints.Count; index++)
                        points[index + 1] = spline.FitPoints[index];
                    foreach (var bezier in CadSpline.CreateBezierSegments(points))
                    {
                        bounds = bounds
                            .ExpandToInclude(bezier.Control1)
                            .ExpandToInclude(bezier.Control2)
                            .ExpandToInclude(bezier.End);
                    }
                    current = spline.FitPoints[^1];
                    break;
                case CadCompositeBezierSegment bezier:
                    bounds = bounds
                        .ExpandToInclude(bezier.Control1)
                        .ExpandToInclude(bezier.Control2)
                        .ExpandToInclude(bezier.End);
                    current = bezier.End;
                    break;
            }
        }

        var flattened = EnumerateFlattenedPoints().ToArray();
        var length = 0.0;
        for (var index = 1; index < flattened.Length; index++)
        {
            length += flattened[index - 1].DistanceTo(flattened[index]);
        }

        _bounds = bounds;
        _length = length;
    }

    private static bool ContainsArcAngle(double start, double sweep, double target)
    {
        const double twoPi = Math.PI * 2.0;
        static double Normalize(double angle)
        {
            var normalized = angle % (Math.PI * 2.0);
            return normalized < 0 ? normalized + Math.PI * 2.0 : normalized;
        }

        if (Math.Abs(Math.Abs(sweep) - twoPi) <= 1e-12)
            return true;
        return sweep > 0
            ? Normalize(target - start) <= sweep + 1e-12
            : Normalize(start - target) <= -sweep + 1e-12;
    }

    private static CadPointD RotateAround(CadPointD point, CadPointD center, double angle)
    {
        var x = point.X - center.X;
        var y = point.Y - center.Y;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return new CadPointD(
            center.X + x * cosine - y * sine,
            center.Y + x * sine + y * cosine);
    }
}
