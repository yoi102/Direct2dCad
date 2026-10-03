using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

public enum CadBooleanOperation { Union, Intersection, Difference }

/// <summary>Analytic boundary arrangement. No tessellation is used to decide topology or store results.</summary>
public static class CadRegionBoolean
{
    public const int MaximumInputEdges = 2048;
    public static bool Supports(CadEntity entity) => entity switch
    {
        CadRegion => true, CadCircle => true, CadArc a => a.IsFullCircle,
        CadRectangle r => !r.HasRoundedCorners,
        CadPolyline p => p.Closed && p.Points.Count >= 3,
        CadCompositePath p => p.Closed && p.Segments.All(s => s is CadCompositeLineSegment or CadCompositeArcSegment),
        _ => false
    };

    public static IReadOnlyList<CadRegionContour> GetContours(CadEntity entity)
    {
        if (!Supports(entity)) throw new NotSupportedException("Boolean operations require closed line/circular-arc boundaries or regions.");
        return entity is CadRegion region ? region.Contours : [new CadRegionContour(CadPlanarCurves.Get(entity).Where(p => !p.IsLine || !CadGeometryTolerance.Coincident(p.Start, p.End)))];
    }

    public static IReadOnlyList<CadRegionContour> Compute(IReadOnlyList<CadEntity> operands, CadBooleanOperation operation, int subject = 0) =>
        Compute(operands.Select(GetContours).ToArray(), operation, subject);

    public static IReadOnlyList<CadRegionContour> Compute(IReadOnlyList<IReadOnlyList<CadRegionContour>> operands, CadBooleanOperation operation, int subject = 0, CancellationToken token = default)
    {
        if (!Enum.IsDefined(operation) || operands.Count < 2 || subject < 0 || subject >= operands.Count)
            throw new ArgumentException("At least two operands and a valid operation/subject are required.");
        token.ThrowIfCancellationRequested();
        var shapes = operands.ToArray();
        if (shapes.Sum(s => s.Sum(c => (long)c.Edges.Count)) > MaximumInputEdges)
            throw new NotSupportedException("Boolean input exceeds the 2048-edge operation budget.");
        foreach (var shape in shapes) ValidateSimple(shape, token);
        var edges = shapes.SelectMany(s => s).SelectMany(c => c.Edges).ToArray();
        var cuts = edges.Select(p => new List<double> { 0, 1 }).ToArray();
        var cutCount = edges.Length * 2;
        for (var i = 0; i < edges.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!edges[i].IsLine)
                foreach (var a in new[] { 0d, Math.PI / 2, Math.PI, Math.PI * 1.5 }) AddCut(i, CadPlanarPrimitive.Point(edges[i].Center, edges[i].Radius, a));
            for (var j = i + 1; j < edges.Length; j++)
            {
                if ((j & 127) == 0) token.ThrowIfCancellationRequested();
                foreach (var q in Nodes(edges[i], edges[j])) { AddCut(i, q); AddCut(j, q); }
            }
        }
        void AddCut(int i, CadPointD point)
        {
            var p = edges[i]; var t = p.Parameter(point);
            if (t > 1e-11 && t < 1 - 1e-11 && CadRegionGeometry.Distance(p, point) <= CadGeometryTolerance.For(Math.Max(Math.Abs(point.X), Math.Abs(point.Y))) * 4 && !cuts[i].Any(v => Math.Abs(v - t) <= 1e-11))
            {
                cuts[i].Add(t);
                if (++cutCount > 32768) throw new NotSupportedException("Boolean arrangement exceeds its edge budget.");
            }
        }
        bool Material(CadPointD q)
        {
            return operation switch
            {
                CadBooleanOperation.Union => shapes.Any(s => CadRegionGeometry.Contains(s, q)),
                CadBooleanOperation.Intersection => shapes.All(s => CadRegionGeometry.Contains(s, q)),
                _ => CadRegionGeometry.Contains(shapes[subject], q) && !shapes.Where((_, i) => i != subject).Any(s => CadRegionGeometry.Contains(s, q))
            };
        }
        var boundary = new List<CadPlanarPrimitive>();
        for (var i = 0; i < edges.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var ts = cuts[i].Order().Aggregate(new List<double>(), (list, t) => { if (list.Count == 0 || t - list[^1] > 1e-11) list.Add(t); return list; });
            for (var j = 1; j < ts.Count; j++)
            {
                token.ThrowIfCancellationRequested();
                var p = Slice(edges[i], ts[j - 1], ts[j]); var mid = p.At(.5);
                var tolerance = CadGeometryTolerance.For(Math.Max(Math.Abs(mid.X), Math.Abs(mid.Y)));
                var length = p.IsLine ? p.Start.DistanceTo(p.End) : p.Radius * Math.Abs(p.Sweep);
                if (length <= tolerance * 2) continue;
                var tangent = Tangent(p, .5); var normal = new CadVectorD(-tangent.Y, tangent.X);
                var clearance = edges.Select(e => CadRegionGeometry.Distance(e, mid)).Where(d => d > tolerance * 4).DefaultIfEmpty(length).Min();
                var epsilon = Math.Min(Math.Max(length * 1e-4, tolerance * 8), Math.Min(length * .1, clearance * .2));
                if (epsilon <= tolerance * 4) throw new InvalidOperationException("Boundary separation is below the geometry tolerance.");
                var left = Material(mid + normal * epsilon); var right = Material(mid - normal * epsilon);
                if (left == right) continue;
                if (!left) p = Reverse(p);
                if (!boundary.Any(e => Same(e, p, tolerance * 4))) boundary.Add(p);
                if (boundary.Count > 32768) throw new NotSupportedException("Boolean result exceeds the edge budget.");
            }
        }
        var contours = new List<CadRegionContour>();
        while (boundary.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var loop = new List<CadPlanarPrimitive> { boundary[0] }; boundary.RemoveAt(0);
            while (!CadGeometryTolerance.Coincident(loop[^1].End, loop[0].Start))
            {
                token.ThrowIfCancellationRequested();
                var last = loop[^1]; var tangent = Tangent(last, 1);
                var candidates = boundary.Select((p, i) => (p, i)).Where(x => CadGeometryTolerance.Coincident(x.p.Start, last.End))
                    .OrderBy(x => CadPlanarPrimitive.PositiveAngle(Math.Atan2(CadPlanarGeometry.Cross(tangent, Tangent(x.p, 0)), tangent.Dot(Tangent(x.p, 0))))).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException("Could not close the result boundary within geometry tolerance.");
                loop.Add(candidates[0].p); boundary.RemoveAt(candidates[0].i);
            }
            contours.Add(new CadRegionContour(loop));
        }
        return contours.AsReadOnly();
    }

    private static void ValidateSimple(IReadOnlyList<CadRegionContour> contours, CancellationToken token)
    {
        var all = contours.SelectMany((c, ci) => c.Edges.Select((p, pi) => (p, ci, pi, count: c.Edges.Count))).ToArray();
        for (var i = 0; i < all.Length; i++) for (var j = i + 1; j < all.Length; j++)
        {
            if ((j & 127) == 0) token.ThrowIfCancellationRequested();
            var a = all[i]; var b = all[j];
            var adjacent = a.ci == b.ci && (b.pi == a.pi + 1 || a.pi == 0 && b.pi == a.count - 1);
            foreach (var q in Nodes(a.p, b.p))
            {
                var atAEnd = CadGeometryTolerance.Coincident(q, a.p.Start) || CadGeometryTolerance.Coincident(q, a.p.End);
                var atBEnd = CadGeometryTolerance.Coincident(q, b.p.Start) || CadGeometryTolerance.Coincident(q, b.p.End);
                if (!atAEnd || !atBEnd || (!adjacent && a.ci == b.ci))
                    throw new InvalidOperationException("Self-intersecting or overlapping contours cannot be used in a Boolean operation.");
            }
            if (CadRegionGeometry.Distance(a.p, b.p.At(.5)) <= CadGeometryTolerance.Absolute &&
                CadRegionGeometry.Distance(b.p, a.p.At(.5)) <= CadGeometryTolerance.Absolute)
                throw new InvalidOperationException("Overlapping boundaries within an operand are not supported.");
        }
    }
    private static IEnumerable<CadPointD> Nodes(CadPlanarPrimitive a, CadPlanarPrimitive b)
    {
        foreach (var q in CadPlanarGeometry.Intersections(a, b)) yield return q;
        // Node collinear and coincident-circle overlaps at both operands' endpoints.
        foreach (var q in new[] { a.Start, a.End, b.Start, b.End })
            if (CadRegionGeometry.Distance(a, q) <= CadGeometryTolerance.Absolute && CadRegionGeometry.Distance(b, q) <= CadGeometryTolerance.Absolute) yield return q;
    }
    private static CadPlanarPrimitive Slice(CadPlanarPrimitive p, double a, double b) => p.IsLine ? CadPlanarPrimitive.Line(p.At(a), p.At(b)) : CadPlanarPrimitive.Arc(p.Center, p.Radius, p.StartAngle + p.Sweep * a, p.Sweep * (b - a));
    private static CadPlanarPrimitive Reverse(CadPlanarPrimitive p) => p.IsLine ? CadPlanarPrimitive.Line(p.End, p.Start) : CadPlanarPrimitive.Arc(p.Center, p.Radius, p.StartAngle + p.Sweep, -p.Sweep);
    private static bool Same(CadPlanarPrimitive a, CadPlanarPrimitive b, double tolerance) => a.IsLine == b.IsLine && a.Start.DistanceTo(b.Start) <= tolerance && a.End.DistanceTo(b.End) <= tolerance && a.At(.5).DistanceTo(b.At(.5)) <= tolerance;
    private static CadVectorD Tangent(CadPlanarPrimitive p, double t) => p.IsLine ? (p.End - p.Start).Normalize() : new CadVectorD(-Math.Sin(p.StartAngle + p.Sweep * t) * Math.Sign(p.Sweep), Math.Cos(p.StartAngle + p.Sweep * t) * Math.Sign(p.Sweep));
}
