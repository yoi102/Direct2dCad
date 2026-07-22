using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadPolyline : Curve
{
    private readonly List<CadPointD> _points = new();
    private CadRectD _bounds = CadRectD.Empty;
    private double _openLength;

    public IReadOnlyList<CadPointD> Points => _points;
    public bool Closed { get; private set; }

    public override bool IsClosed => Closed;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    public override double Length => _openLength +
        (Closed && _points.Count > 1 ? _points[^1].DistanceTo(_points[0]) : 0.0);

    public override CadRectD Bounds => _bounds;

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

    public void AddPoint(CadPointD point)
    {
        if (_points.Count > 0)
            _openLength += _points[^1].DistanceTo(point);
        _points.Add(point);
        _bounds = _bounds.ExpandToInclude(point);
    }

    public bool RemovePoint(CadPointD point)
    {
        if (!_points.Remove(point))
            return false;

        RebuildDerivedGeometry();
        return true;
    }

    public void SetClosed(bool closed) => Closed = closed;

    public void ReplacePoints(IEnumerable<CadPointD> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var list = points.ToArray();
        if (list.Length < 2)
            throw new ArgumentException("Polyline requires at least two points.", nameof(points));

        _points.Clear();
        _points.AddRange(list);
        RebuildDerivedGeometry();
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;

    private void RebuildDerivedGeometry()
    {
        var bounds = CadRectD.Empty;
        var length = 0.0;
        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];
            bounds = bounds.ExpandToInclude(point);
            if (index > 0)
                length += _points[index - 1].DistanceTo(point);
        }

        _bounds = bounds;
        _openLength = length;
    }
}
