using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadSpline : Curve
{
    private const int DefaultFlattenStepsPerSegment = 20;
    private readonly List<CadPointD> _fitPoints = [];

    public IReadOnlyList<CadPointD> FitPoints => _fitPoints;
    public bool Closed { get; private set; }

    public override bool IsClosed => Closed;

    public StyleId? GraphicStyleId { get; private set; }

    public override double Length
    {
        get
        {
            var flattened = EnumerateFlattenedPoints(DefaultFlattenStepsPerSegment).ToArray();
            if (flattened.Length < 2)
                return 0;

            var length = 0.0;
            for (var i = 1; i < flattened.Length; i++)
                length += flattened[i - 1].DistanceTo(flattened[i]);

            return length;
        }
    }

    public override CadRectD Bounds
    {
        get
        {
            var bounds = CadRectD.Empty;
            foreach (var point in _fitPoints)
                bounds = bounds.ExpandToInclude(point);

            foreach (var segment in GetBezierSegments())
            {
                bounds = bounds
                    .ExpandToInclude(segment.Control1)
                    .ExpandToInclude(segment.Control2)
                    .ExpandToInclude(segment.End);
            }

            return bounds;
        }
    }

    internal CadSpline(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        ReplaceFitPoints(fitPoints);
        SetClosed(closed);
    }

    public void ReplaceFitPoints(IEnumerable<CadPointD> fitPoints)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        var list = fitPoints.ToArray();
        if (list.Length < 2)
            throw new ArgumentException("Spline requires at least two fit points.", nameof(fitPoints));

        _fitPoints.Clear();
        _fitPoints.AddRange(list);

        if (Closed && _fitPoints.Count < 3)
            Closed = false;
    }

    public void SetClosed(bool closed)
    {
        if (closed && _fitPoints.Count < 3)
            throw new InvalidOperationException("Closed spline requires at least three fit points.");

        Closed = closed;
    }

    public IReadOnlyList<CadBezierSegmentD> GetBezierSegments()
    {
        return CreateBezierSegments(_fitPoints, Closed);
    }

    public IEnumerable<CadPointD> EnumerateFlattenedPoints(int stepsPerSegment = DefaultFlattenStepsPerSegment)
    {
        stepsPerSegment = Math.Max(1, stepsPerSegment);
        var segments = GetBezierSegments();
        if (segments.Count == 0)
            yield break;

        yield return segments[0].Start;
        foreach (var segment in segments)
        {
            for (var step = 1; step <= stepsPerSegment; step++)
                yield return segment.Evaluate((double)step / stepsPerSegment);
        }
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

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
