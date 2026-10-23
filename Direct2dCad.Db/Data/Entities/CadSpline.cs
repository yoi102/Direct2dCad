using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadSpline : Curve
{
    private const int DefaultFlattenStepsPerSegment = 20;
    private CadSplineGeometry _geometry = null!;
    private IReadOnlyList<CadBezierSegmentD>? _bezierSegments;
    private CadRectD _bounds = CadRectD.Empty;
    private double? _length;

    public CadSplineGeometry Geometry => _geometry;
    public CadNurbsCurve Nurbs => _geometry.Curve;
    public IReadOnlyList<CadPointD> FitPoints => _geometry.FitPoints;
    public IReadOnlyList<CadPointD> ControlPoints => Nurbs.ControlPoints;
    public IReadOnlyList<double> Knots => Nurbs.Knots;
    public IReadOnlyList<double> Weights => Nurbs.Weights;
    public int Degree => Nurbs.Degree;
    public IReadOnlyList<CadPointD> EditPoints => _geometry.EditPoints;
    public bool Closed => _geometry.Closed;

    public override bool IsClosed => Closed;

    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? FillStyleId { get; private set; }

    public override double Length
    {
        get
        {
            if (_length is { } cached)
                return cached;
            _length = CadCurveMeasurements.Measure(this).Length;
            return _length.Value;
        }
    }

    public override CadRectD Bounds => _bounds;

    internal CadSpline(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        SetGeometry(CadSplineGeometry.FromFitPoints(fitPoints, closed));
    }

    internal CadSpline(EntityId id, LayerId layerId, BlockId ownerBlockId, CadSplineGeometry geometry, string name = "")
        : base(id, layerId, ownerBlockId, name) => SetGeometry(geometry);

    public void SetGeometry(CadSplineGeometry geometry)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        RebuildDerivedGeometry();
    }

    public void ReplaceFitPoints(IEnumerable<CadPointD> fitPoints)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        var list = fitPoints.ToArray();
        if (list.Length < 2)
            throw new ArgumentException("Spline requires at least two fit points.", nameof(fitPoints));

        SetGeometry(CadSplineGeometry.FromFitPoints(list, Closed && list.Length >= 3));
    }

    public void SetClosed(bool closed)
    {
        if (closed && EditPoints.Count < 3)
            throw new InvalidOperationException("Closed spline requires at least three fit points.");

        if (Closed == closed)
            return;

        SetGeometry(_geometry.WithClosed(closed));
    }

    public IReadOnlyList<CadBezierSegmentD> GetBezierSegments()
    {
        return _bezierSegments ??= Nurbs.CreateDisplayBeziers();
    }

    public IEnumerable<CadPointD> EnumerateFlattenedPoints(int stepsPerSegment = DefaultFlattenStepsPerSegment)
    {
        stepsPerSegment = Math.Max(1, stepsPerSegment);
        yield return Nurbs.Evaluate(Nurbs.ParameterStart);
        foreach (var span in Nurbs.ParameterSpans())
        {
            for (var step = 1; step <= stepsPerSegment; step++)
                yield return Nurbs.Evaluate(span.Start + (span.End - span.Start) * step / stepsPerSegment);
        }
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;

    public static IReadOnlyList<CadBezierSegmentD> CreateBezierSegments(
        IReadOnlyList<CadPointD> fitPoints,
        bool closed = false)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        if (fitPoints.Count < 2 || (closed && fitPoints.Count < 3))
            return [];

        var segmentCount = closed ? fitPoints.Count : fitPoints.Count - 1;
        var segments = new List<CadBezierSegmentD>(segmentCount);

        for (var i = 0; i < segmentCount; i++)
        {
            var p0 = GetPoint(fitPoints, i - 1, closed);
            var p1 = GetPoint(fitPoints, i, closed);
            var p2 = GetPoint(fitPoints, i + 1, closed);
            var p3 = GetPoint(fitPoints, i + 2, closed);
            var control1 = new CadPointD(
                p1.X + (p2.X - p0.X) / 6.0,
                p1.Y + (p2.Y - p0.Y) / 6.0);
            var control2 = new CadPointD(
                p2.X - (p3.X - p1.X) / 6.0,
                p2.Y - (p3.Y - p1.Y) / 6.0);

            segments.Add(new CadBezierSegmentD(p1, control1, control2, p2));
        }

        return segments;
    }

    private void RebuildDerivedGeometry()
    {
        _length = null;
        _bezierSegments = null;
        _bounds = Nurbs.Bounds;
    }

    public static CadRectD CalculateBounds(IReadOnlyList<CadPointD> fitPoints, IReadOnlyList<CadBezierSegmentD> segments)
    {
        var bounds = CadRectD.Empty;
        foreach (var point in fitPoints)
            bounds = bounds.ExpandToInclude(point);

        foreach (var segment in segments)
        {
            bounds = bounds
                .ExpandToInclude(segment.Control1)
                .ExpandToInclude(segment.Control2)
                .ExpandToInclude(segment.End);
        }

        return bounds;
    }

    private static CadPointD GetPoint(IReadOnlyList<CadPointD> points, int index, bool closed)
    {
        if (closed)
        {
            var count = points.Count;
            var wrappedIndex = index % count;
            if (wrappedIndex < 0)
                wrappedIndex += count;

            return points[wrappedIndex];
        }

        return points[Math.Clamp(index, 0, points.Count - 1)];
    }
}
