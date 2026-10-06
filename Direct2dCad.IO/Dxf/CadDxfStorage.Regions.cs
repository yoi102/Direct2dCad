using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.IO.Dxf;

public sealed partial class CadDxfStorage
{
    private static bool TryRegionSolidFill(CadDocument document, CadRegion region, out CadColor color)
    {
        if (region.FillStyleId is { } id && document.TryGetStyle(id, out var style) &&
            style is CadGradientFillStyle { IsSolid: true } fill)
        {
            color = fill.Stops[0].Color;
            return true;
        }
        color = default;
        return false;
    }

    private static IReadOnlyList<(CadPointD Point, double Bulge)> RegionVertices(CadRegionContour contour, CancellationToken token)
    {
        var vertices = new List<(CadPointD, double)>(contour.Edges.Count);
        foreach (var edge in contour.Edges)
        {
            token.ThrowIfCancellationRequested();
            if (edge.IsEllipse) throw new InvalidOperationException("An ellipse boundary cannot be encoded as a polyline bulge.");
            if (edge.IsLine) vertices.Add((edge.Start, 0));
            else
            {
                // A full circle has coincident endpoints and tan(2*pi/4) is unbounded.
                // Splitting at <= pi preserves exact arcs, orientation and finite bulges.
                var count = (int)Math.Ceiling(Math.Abs(edge.Sweep) / Math.PI);
                var bulge = Math.Tan(edge.Sweep / count / 4);
                for (var i = 0; i < count; i++) vertices.Add((i == 0 ? edge.Start : edge.At((double)i / count), bulge));
            }
        }
        return vertices;
    }

    private static (CadVectorD Major, double Ratio, double Start, double Sweep) EllipseParameters(CadPlanarPrimitive edge)
    {
        // DXF requires the first axis to be the major axis. Swapping axes rotates
        // the frame by pi/2 and subtracts pi/2 from the parameter, preserving points.
        var swap = edge.RadiusY > edge.RadiusX;
        var radius = swap ? edge.RadiusY : edge.RadiusX;
        var rotation = edge.Rotation + (swap ? Math.PI / 2 : 0);
        return (new CadVectorD(radius * Math.Cos(rotation), radius * Math.Sin(rotation)),
            Math.Min(edge.RadiusX, edge.RadiusY) / radius,
            edge.StartAngle - (swap ? Math.PI / 2 : 0), edge.Sweep);
    }

    private static void WriteEllipticalRegion(CadRegion region, double factor,
        CadColor? solidFill, Action<int, object> pair, Action<CadEntity, string, CadColor?> common,
        CancellationToken token)
    {
        void Point(CadPointD point, int code = 10)
        { pair(code, point.X * factor); pair(code + 10, point.Y * factor); }
        void Ellipse(CadPlanarPrimitive edge, bool hatch)
        {
            var e = EllipseParameters(edge);
            var start = CadPlanarPrimitive.PositiveAngle(e.Sweep < 0 ? e.Start + e.Sweep : e.Start);
            var end = start + Math.Abs(e.Sweep);
            Point(edge.Center); Point(new(e.Major.X, e.Major.Y), 11); pair(40, e.Ratio);
            if (hatch)
            {
                // Unlike ELLIPSE (parameters), HATCH ellipse edges use polar angles
                // in the local major-axis frame. Clockwise DXF angles are complementary.
                var a = CadPlanarPrimitive.PositiveAngle(Math.Atan2(e.Ratio * Math.Sin(start), Math.Cos(start)));
                var b = Math.Abs(e.Sweep) >= Math.PI * 2 - 1e-10 ? a + Math.PI * 2
                    : a + CadPlanarPrimitive.PositiveAngle(Math.Atan2(e.Ratio * Math.Sin(end), Math.Cos(end)) - a);
                HatchAngles(a, b, edge.Sweep > 0);
            }
            else { pair(41, start); pair(42, end); }
        }
        void HatchAngles(double start, double end, bool counterclockwise)
        {
            var a = start * 180 / Math.PI; var b = end * 180 / Math.PI;
            pair(50, counterclockwise ? a : 360 - b);
            pair(51, counterclockwise ? b : 360 - a);
            pair(73, counterclockwise ? 1 : 0);
        }
        if (solidFill is { } color)
        {
            common(region, "HATCH", color); Point(default); pair(30, 0);
            pair(210, 0); pair(220, 0); pair(230, 1);
            pair(2, "SOLID"); pair(70, 1); pair(71, 0); pair(91, region.Contours.Count);
            foreach (var contour in region.Contours)
            {
                token.ThrowIfCancellationRequested();
                // Polar-angle conversion normalizes a full ellipse's two endpoints
                // to the same parameter in DXF readers. Two exact half-ellipse edges
                // retain the original seam and direction without that ambiguity.
                var edgeCount = contour.Edges.Sum(edge => edge.IsEllipse && Math.Abs(edge.Sweep) >= Math.PI * 2 - 1e-10 ? 2 : 1);
                pair(92, 0); pair(93, edgeCount);
                foreach (var edge in contour.Edges)
                {
                    token.ThrowIfCancellationRequested();
                    if (edge.IsLine) { pair(72, 1); Point(edge.Start); Point(edge.End, 11); }
                    else if (edge.IsEllipse)
                    {
                        if (Math.Abs(edge.Sweep) >= Math.PI * 2 - 1e-10)
                        {
                            pair(72, 3); Ellipse(edge.Slice(0, .5), true);
                            pair(72, 3); Ellipse(edge.Slice(.5, 1), true);
                        }
                        else { pair(72, 3); Ellipse(edge, true); }
                    }
                    else
                    {
                        pair(72, 2); Point(edge.Center); pair(40, edge.Radius * factor);
                        var start = CadPlanarPrimitive.PositiveAngle(edge.Sweep < 0 ? edge.StartAngle + edge.Sweep : edge.StartAngle);
                        HatchAngles(start, start + Math.Abs(edge.Sweep), edge.Sweep > 0);
                    }
                }
                pair(97, 0);
            }
            pair(75, 0); pair(76, 1); pair(98, 0);
        }
        foreach (var edge in region.Contours.SelectMany(c => c.Edges))
        {
            token.ThrowIfCancellationRequested();
            if (edge.IsLine) { common(region, "LINE", null); Point(edge.Start); Point(edge.End, 11); }
            else if (edge.IsEllipse) { common(region, "ELLIPSE", null); Ellipse(edge, false); }
            else
            {
                var full = Math.Abs(edge.Sweep) >= Math.PI * 2 - 1e-10;
                common(region, full ? "CIRCLE" : "ARC", null); Point(edge.Center); pair(40, edge.Radius * factor);
                if (full) continue;
                var start = edge.Sweep < 0 ? edge.StartAngle + edge.Sweep : edge.StartAngle;
                pair(100, "AcDbArc"); pair(50, CadPlanarPrimitive.PositiveAngle(start) * 180 / Math.PI);
                pair(51, CadPlanarPrimitive.PositiveAngle(start + Math.Abs(edge.Sweep)) * 180 / Math.PI);
            }
        }
    }
}
