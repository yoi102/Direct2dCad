using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public static partial class CadCurveEditing
{
    public static int PickSegment(CadEntity entity, CadPointD pick) => PickSegment(CadCurveShape.From(entity).Validate(), pick);

    private static int PickSegment(CadCurveShape shape, CadPointD pick)
    {
        var best = double.MaxValue;
        var index = 0;
        for (var i = 0; i < shape.Segments.Count; i++)
        {
            var p = shape.Segments[i];
            var t = Math.Clamp(p.Parameter(pick), 0, 1);
            if (!p.IsLine && !p.Contains(pick)) t = p.Start.DistanceTo(pick) < p.End.DistanceTo(pick) ? 0 : 1;
            var distance = p.At(t).DistanceTo(pick);
            if (distance < best) { best = distance; index = i; }
        }
        return index;
    }

    private static CadCurveEditPlan PathCorner(CadEntity first, CadEntity second, CadPointD pickA, CadPointD pickB,
        double size, double secondDistance, bool fillet, bool trim)
    {
        ValidateCornerSizes(size, secondDistance);
        if (first.OwnerBlockId != second.OwnerBlockId) throw new InvalidOperationException("The curves must share an owner space.");
        var a = CadCurveShape.From(first).Validate(); var b = CadCurveShape.From(second).Validate();
        var ia = PickSegment(a, pickA); var ib = PickSegment(b, pickB);
        if (first.Id == second.Id)
        {
            if (ia == ib) throw new InvalidOperationException("Choose two adjacent segments of the path.");
            if ((ia + 1) % a.Segments.Count == ib && (a.Closed || ia + 1 < a.Segments.Count)) { }
            else if ((ib + 1) % a.Segments.Count == ia && (a.Closed || ib + 1 < a.Segments.Count))
            {
                (ia, ib, pickA, pickB) = (ib, ia, pickB, pickA);
                if (!fillet) (size, secondDistance) = (secondDistance, size);
            }
            else throw new InvalidOperationException("Choose two adjacent segments of the path.");
            var corner = SegmentCorner(a.Segments[ia], a.Segments[ib], a.Segments[ia].Start, a.Segments[ib].End,
                size, secondDistance, fillet);
            if (!trim) return new([], [new(first.Id, new([corner.Connector]))]);
            var starts = new double[a.Segments.Count]; var ends = Enumerable.Repeat(1.0, a.Segments.Count).ToArray();
            ends[ia] = a.Segments[ia].Parameter(corner.First.End);
            starts[ib] = a.Segments[ib].Parameter(corner.Second.Start);
            var result = RebuildCorners(a, starts, ends, new Dictionary<int, CadPlanarPrimitive> { [ia] = corner.Connector });
            return CadCurveEditPlan.Replace(first.Id, result) with { PreviewGeometry = [result] };
        }
        if (trim)
        {
            EnsureEndpointSegment(a, ia); EnsureEndpointSegment(b, ib);
        }
        // At a path endpoint retain the rest of the chain, irrespective of pick proximity.
        var retainedA = a.Segments.Count > 1 ? ia == 0 ? a.Segments[ia].End : a.Segments[ia].Start : pickA;
        var retainedB = b.Segments.Count > 1 ? ib == 0 ? b.Segments[ib].End : b.Segments[ib].Start : pickB;
        var pieces = SegmentCorner(a.Segments[ia], b.Segments[ib], retainedA, retainedB, size, secondDistance, fillet);
        if (!trim) return new([], [new(first.Id, new([pieces.Connector]))]);
        var updatedA = a.Segments.ToArray(); var updatedB = b.Segments.ToArray();
        updatedA[ia] = OrientLike(pieces.First, a.Segments[ia]);
        updatedB[ib] = OrientLike(pieces.Second, b.Segments[ib]);
        return new([new(first.Id, [new CadCurveShape(updatedA, a.Closed).Validate()]),
                    new(second.Id, [new CadCurveShape(updatedB, b.Closed).Validate()])],
                   [new(first.Id, new([pieces.Connector]))]);
    }

    public static CadCurveEditPlan AllCorners(CadEntity entity, double size, double secondDistance, bool fillet)
    {
        ValidateCornerSizes(size, secondDistance);
        var shape = CadCurveShape.From(entity).Validate();
        if (shape.Segments.Count < 2) throw new InvalidOperationException("Choose a polyline or a line/arc path with corners.");
        var starts = new double[shape.Segments.Count]; var ends = Enumerable.Repeat(1.0, shape.Segments.Count).ToArray();
        var connectors = new Dictionary<int, CadPlanarPrimitive>();
        for (var i = 0; i < (shape.Closed ? shape.Segments.Count : shape.Segments.Count - 1); i++)
        {
            var next = (i + 1) % shape.Segments.Count;
            var a = shape.Segments[i]; var b = shape.Segments[next];
            var incoming = Tangent(a, 1); var outgoing = Tangent(b, 0);
            // Smooth joins and collinear continuations already have no sharp corner.
            if (Math.Abs(incoming.Cross(outgoing)) < 1e-9 && incoming.Dot(outgoing) > 0) continue;
            var corner = SegmentCorner(a, b, a.Start, b.End, size, secondDistance, fillet);
            ends[i] = a.Parameter(corner.First.End); starts[next] = b.Parameter(corner.Second.Start);
            connectors[i] = corner.Connector;
        }
        if (connectors.Count == 0) throw new InvalidOperationException("This path has no sharp corners to modify.");
        var result = RebuildCorners(shape, starts, ends, connectors);
        return CadCurveEditPlan.Replace(entity.Id, result) with { PreviewGeometry = [result] };
    }

    private static CadVectorD Tangent(CadPlanarPrimitive p, double t) => p.IsLine ? (p.End - p.Start).Normalize() :
        (p.At(t) - p.Center).Normalize().Perpendicular() * Math.Sign(p.Sweep);

    private static void ValidateCornerSizes(double size, double secondDistance)
    {
        if (!double.IsFinite(size) || size <= CadGeometryTolerance.Absolute ||
            !double.IsFinite(secondDistance) || secondDistance <= CadGeometryTolerance.Absolute)
            throw new ArgumentOutOfRangeException(nameof(size));
    }

    private static void EnsureEndpointSegment(CadCurveShape shape, int index)
    {
        if (shape.Closed || index != 0 && index != shape.Segments.Count - 1)
            throw new InvalidOperationException("Choose an endpoint segment to connect different paths; choose adjacent segments for an internal corner.");
    }

    private static CadPlanarPrimitive OrientLike(CadPlanarPrimitive piece, CadPlanarPrimitive original) =>
        CadGeometryTolerance.Coincident(piece.Start, original.Start) || CadGeometryTolerance.Coincident(piece.End, original.End) ? piece : Reverse(piece);

    private static CadCurveShape RebuildCorners(CadCurveShape shape, double[] starts, double[] ends,
        IReadOnlyDictionary<int, CadPlanarPrimitive> connectors)
    {
        var result = new List<CadPlanarPrimitive>();
        for (var i = 0; i < shape.Segments.Count; i++)
        {
            if (starts[i] < -1e-9 || ends[i] > 1 + 1e-9 || ends[i] - starts[i] <= 1e-9)
                throw new InvalidOperationException("The corner sizes overlap or exceed a path segment; reduce the sizes.");
            result.AddRange(Slice(new([shape.Segments[i]]), starts[i], ends[i]).Segments);
            if (connectors.TryGetValue(i, out var connector)) result.Add(connector);
        }
        var rebuilt = new CadCurveShape(result, shape.Closed).Validate();
        for (var i = 0; i < result.Count; i++) for (var j = i + 2; j < result.Count; j++)
            if (!(shape.Closed && i == 0 && j == result.Count - 1) && CadPlanarGeometry.Intersections(result[i], result[j]).Count > 0)
                throw new InvalidOperationException("The corner would self-intersect the path; reduce the sizes.");
        return rebuilt;
    }

    private static (CadPlanarPrimitive First, CadPlanarPrimitive Connector, CadPlanarPrimitive Second) SegmentCorner(
        CadPlanarPrimitive a, CadPlanarPrimitive b, CadPointD pickA, CadPointD pickB, double size, double secondDistance, bool fillet)
    {
        CadPointD pa, pb, center = default;
        if (a.IsLine && b.IsLine)
        {
            var intersections = CadPlanarGeometry.Intersections(a, b, true, true);
            if (intersections.Count == 0) throw new InvalidOperationException("Parallel segments cannot form a corner.");
            var intersection = intersections[0];
            var endA = a.Start.DistanceTo(pickA) < a.End.DistanceTo(pickA) ? a.Start : a.End;
            var endB = b.Start.DistanceTo(pickB) < b.End.DistanceTo(pickB) ? b.Start : b.End;
            var u = (endA - intersection).Normalize(); var v = (endB - intersection).Normalize();
            var angle = Math.Acos(Math.Clamp(u.Dot(v), -1, 1));
            if (angle < 1e-8 || Math.PI - angle < 1e-8) throw new InvalidOperationException("The selected directions do not form a corner.");
            var da = fillet ? size / Math.Tan(angle / 2) : size; var db = fillet ? da : secondDistance;
            if (da >= endA.DistanceTo(intersection) - CadGeometryTolerance.Absolute || db >= endB.DistanceTo(intersection) - CadGeometryTolerance.Absolute)
                throw new InvalidOperationException("The corner size exceeds the selected segment lengths.");
            pa = intersection + u * da; pb = intersection + v * db;
            if (fillet) center = intersection + (u + v).Normalize() * (size / Math.Sin(angle / 2));
        }
        else if (fillet)
        {
            var candidates = new List<(CadPointD Center, CadPointD A, CadPointD B)>();
            foreach (var la in Loci(a, size)) foreach (var lb in Loci(b, size))
                foreach (var c in CadPlanarGeometry.Intersections(la, lb, true, true))
                {
                    var ta = TangentPoint(a, c); var tb = TangentPoint(b, c);
                    if (a.Contains(ta) && b.Contains(tb) && Math.Abs(c.DistanceTo(ta) - size) <= CadGeometryTolerance.For(size) * 10 &&
                        Math.Abs(c.DistanceTo(tb) - size) <= CadGeometryTolerance.For(size) * 10 && !CadGeometryTolerance.Coincident(ta, tb))
                        candidates.Add((c, ta, tb));
                }
            if (candidates.Count == 0) throw new InvalidOperationException("No tangent fillet fits these segments; reduce the radius.");
            var best = candidates.OrderBy(c => c.A.DistanceTo(pickA) + c.B.DistanceTo(pickB)).ThenBy(c => c.Center.X).ThenBy(c => c.Center.Y).First();
            (center, pa, pb) = best;
        }
        else
        {
            var intersections = CadPlanarGeometry.Intersections(a, b, a.IsLine, b.IsLine);
            if (intersections.Count == 0) throw new InvalidOperationException("These segments have no corner intersection.");
            var corner = intersections.OrderBy(p => p.DistanceTo(pickA) + p.DistanceTo(pickB)).First();
            pa = ChamferPoint(a, corner, pickA, size); pb = ChamferPoint(b, corner, pickB, secondDistance);
        }
        CadPlanarPrimitive connector;
        if (fillet)
        {
            var angle = Math.Atan2(pa.Y - center.Y, pa.X - center.X);
            var sweep = CadPlanarPrimitive.PositiveAngle(Math.Atan2(pb.Y - center.Y, pb.X - center.X) - angle);
            if (sweep > Math.PI) sweep -= Math.PI * 2;
            connector = CadPlanarPrimitive.Arc(center, size, angle, sweep);
        }
        else connector = CadPlanarPrimitive.Line(pa, pb);
        CadPlanarPrimitive Retain(CadPlanarPrimitive p,CadPointD tangent,CadPointD pick) => p.IsLine
            ? pick.DistanceTo(p.Start)<pick.DistanceTo(p.End) ? CadPlanarPrimitive.Line(p.Start,tangent) : CadPlanarPrimitive.Line(tangent,p.End)
            : KeepPickedEnd(p,tangent,pick).Segments[0];
        return (Retain(a,pa,pickA),connector,Retain(b,pb,pickB));
    }
}
