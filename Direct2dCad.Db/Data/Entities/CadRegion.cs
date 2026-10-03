using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

/// <summary>An immutable closed boundary of exact lines and circular arcs.</summary>
public sealed class CadRegionContour
{
    public IReadOnlyList<CadPlanarPrimitive> Edges { get; }
    public double SignedArea { get; }
    public double Length { get; }
    public CadRectD Bounds { get; }

    public CadRegionContour(IEnumerable<CadPlanarPrimitive> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var array = edges.ToArray();
        if (array.Length == 0 || array.Length > 32768) throw new ArgumentException("A contour requires 1–32768 edges.", nameof(edges));
        var bounds = CadRectD.Empty;
        var origin = array[0].Start;
        double area = 0, length = 0;
        for (var i = 0; i < array.Length; i++)
        {
            var p = array[i];
            CadCompositePath.GuardPoint(p.Start, nameof(edges));
            CadCompositePath.GuardPoint(p.End, nameof(edges));
            if (!CadGeometryTolerance.Coincident(p.End, array[(i + 1) % array.Length].Start))
                throw new ArgumentException("Contour boundaries must be continuous and closed.", nameof(edges));
            var a = p.Start - origin; var b = p.End - origin;
            if (p.IsLine)
            {
                if (p.Start.DistanceTo(p.End) <= CadGeometryTolerance.Absolute) throw new ArgumentException("Zero-length boundary.", nameof(edges));
                area += (a.X * b.Y - b.X * a.Y) / 2;
                length += p.Start.DistanceTo(p.End);
            }
            else
            {
                CadCompositePath.GuardPoint(p.Center, nameof(edges));
                CadCompositePath.GuardSweep(p.Sweep);
                if (!double.IsFinite(p.Radius) || p.Radius <= CadGeometryTolerance.Absolute || !double.IsFinite(p.StartAngle) ||
                    !CadGeometryTolerance.Coincident(p.Start, p.At(0)) || !CadGeometryTolerance.Coincident(p.End, p.At(1)))
                    throw new ArgumentException("Invalid circular boundary.", nameof(edges));
                var c = p.Center - origin;
                area += (c.X * (b.Y - a.Y) - c.Y * (b.X - a.X) + p.Radius * p.Radius * p.Sweep) / 2;
                length += p.Radius * Math.Abs(p.Sweep);
                foreach (var angle in new[] { 0d, Math.PI / 2, Math.PI, Math.PI * 1.5 })
                {
                    var q = CadPlanarPrimitive.Point(p.Center, p.Radius, angle);
                    if (p.Contains(q)) bounds = bounds.ExpandToInclude(q);
                }
            }
            bounds = bounds.ExpandToInclude(p.Start).ExpandToInclude(p.End);
        }
        if (!double.IsFinite(area) || Math.Abs(area) <= CadGeometryTolerance.Absolute * CadGeometryTolerance.Absolute)
            throw new ArgumentException("Contour must enclose a finite area.", nameof(edges));
        Edges = Array.AsReadOnly(array); SignedArea = area; Length = length; Bounds = bounds;
    }

    public CadRegionContour Transform(Func<CadPointD, CadPointD> transform, bool mirrored = false) => new(Edges.Select(p =>
    {
        if (p.IsLine) return CadPlanarPrimitive.Line(transform(p.Start), transform(p.End));
        var start = transform(p.Start); var center = transform(p.Center);
        return CadPlanarPrimitive.Arc(center, start.DistanceTo(center), Math.Atan2(start.Y - center.Y, start.X - center.X), mirrored ? -p.Sweep : p.Sweep);
    }));
}

/// <summary>Even-odd planar material, including holes and disconnected islands.</summary>
public sealed class CadRegion : Curve
{
    private double _area;
    private double _length;
    private CadRectD _bounds;
    public IReadOnlyList<CadRegionContour> Contours { get; private set; } = [];
    public override bool IsClosed => true;
    public override double Length => _length;
    public double Area => _area;
    public override CadRectD Bounds => _bounds;
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? FillStyleId { get; private set; }

    internal CadRegion(EntityId id, LayerId layer, BlockId owner, IEnumerable<CadRegionContour> contours, string name = "") : base(id, layer, owner, name) => ReplaceGeometry(contours);
    public void ReplaceGeometry(IEnumerable<CadRegionContour> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);
        var array = contours.ToArray();
        if (array.Length == 0 || array.Any(c => c is null) || array.Sum(c => (long)c.Edges.Count) > 32768)
            throw new ArgumentException("Region requires non-empty bounded contours.", nameof(contours));
        var area = CadRegionGeometry.Area(array);
        var length = array.Sum(c => c.Length);
        var bounds = array.Aggregate(CadRectD.Empty, (b, c) => b.Union(c.Bounds));
        Contours = Array.AsReadOnly(array);
        _area = area; _length = length; _bounds = bounds;
    }
    public bool Contains(CadPointD point) => CadRegionGeometry.Contains(Contours, point);
    public void SetGraphicStyleInternal(StyleId? id) => GraphicStyleId = id;
    public void SetFillStyleInternal(StyleId? id) => FillStyleId = id;
}
