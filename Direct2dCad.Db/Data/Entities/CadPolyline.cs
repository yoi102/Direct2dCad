using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadPolyline : Curve
{
    private readonly List<CadPointD> _points = new();

    public IReadOnlyList<CadPointD> Points => _points;
    public bool Closed { get; private set; }

    public override bool IsClosed => Closed;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    public override double Length
    {
        get
        {
            if (_points.Count < 2)
                return 0;

            var length = 0.0;
            for (var i = 1; i < _points.Count; i++)
                length += _points[i - 1].DistanceTo(_points[i]);

            if (Closed)
                length += _points[^1].DistanceTo(_points[0]);

            return length;
        }
    }

    public override CadRectD Bounds
    {
        get
        {
            var bounds = CadRectD.Empty;
            foreach (var point in _points)
                bounds = bounds.ExpandToInclude(point);
            return bounds;
        }
    }

    internal CadPolyline(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        IEnumerable<CadPointD> points,
        bool isClosed = false,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        ReplacePoints(points);
        Closed = isClosed;
    }

    public void AddPoint(CadPointD point) => _points.Add(point);

    public bool RemovePoint(CadPointD point) => _points.Remove(point);

    public void SetClosed(bool closed) => Closed = closed;

    public void ReplacePoints(IEnumerable<CadPointD> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var list = points.ToArray();
        if (list.Length < 2)
            throw new ArgumentException("Polyline requires at least two points.", nameof(points));

        _points.Clear();
        _points.AddRange(list);
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;
}
