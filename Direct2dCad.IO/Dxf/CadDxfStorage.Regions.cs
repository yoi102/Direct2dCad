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
}
