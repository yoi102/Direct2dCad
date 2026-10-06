using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

/// <summary>An immutable closed boundary of exact lines, circular arcs and elliptical arcs.</summary>
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
            if (!double.IsFinite(p.EllipseRadiusY) || p.EllipseRadiusY < 0 || !double.IsFinite(p.Rotation) || (p.IsLine && p.IsEllipse))
                throw new ArgumentException("Invalid elliptical boundary parameters.", nameof(edges));
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
                    throw new ArgumentException("Invalid curved boundary.", nameof(edges));
                if (p.IsEllipse && p.RadiusY <= CadGeometryTolerance.Absolute)
                    throw new ArgumentException("Invalid elliptical boundary radius.", nameof(edges));
                var c = p.Center - origin;
                area += (c.X * (b.Y - a.Y) - c.Y * (b.X - a.X) + p.RadiusX * p.RadiusY * p.Sweep) / 2;
                length += p.Length;
                foreach (var t in p.ExtremaParameters()) bounds = bounds.ExpandToInclude(p.At(t));
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
        if (p.IsEllipse)
        {
            // A unit probe loses relative precision when either the source or
            // destination is far from the origin. Affine similarity transforms
            // allow a larger probe before subtracting the transformed center.
            var coordinateScale = Math.Max(Math.Max(Math.Abs(p.Center.X), Math.Abs(p.Center.Y)),
                Math.Max(Math.Abs(center.X), Math.Abs(center.Y)));
            var probe = Math.Max(1, Math.Max(Math.Max(p.RadiusX, p.RadiusY),
                coordinateScale * 1e-4));
            var x = (transform(p.Center + new CadVectorD(Math.Cos(p.Rotation), Math.Sin(p.Rotation)) * probe) - center) / probe;
            var y = (transform(p.Center + new CadVectorD(-Math.Sin(p.Rotation), Math.Cos(p.Rotation)) * probe) - center) / probe;
            var scale = x.Length;
            if (scale <= 0 || Math.Abs(scale - y.Length) > Math.Max(1, scale) * 1e-10 ||
                Math.Abs(x.Dot(y)) > Math.Max(1, scale * scale) * 1e-10)
                throw new NotSupportedException("Elliptical region boundaries require a similarity transform.");
            var reflected = x.Cross(y) < 0;
            return CadPlanarPrimitive.EllipseArc(center, p.RadiusX * scale, p.RadiusY * scale,
                Math.Atan2(x.Y, x.X), reflected ? -p.StartAngle : p.StartAngle, reflected ? -p.Sweep : p.Sweep);
        }
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
