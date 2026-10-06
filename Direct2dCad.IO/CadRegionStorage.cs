using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Common;
using Direct2dCad.IO.FileFormat.Sections;

namespace Direct2dCad.IO;

internal static class CadRegionStorage
{
    private static CadPointData Data(CadPointD p) => new() { X = p.X, Y = p.Y };
    private static CadPointD Point(CadPointData p) => new(p.X, p.Y);
    public static CadRegionsSection Capture(IEnumerable<CadEntity> entities) => new()
    {
        Regions = entities.OfType<CadRegion>().Select(r => new CadRegionData
        {
            Entity = CadDocumentMapper.ToEntityData(r), GraphicStyleId = r.GraphicStyleId?.Value, FillStyleId = r.FillStyleId?.Value,
            Contours = r.Contours.Select(c => c.Edges.Select(p => new CadRegionEdgeData
            { Start = Data(p.Start), End = Data(p.End), Center = Data(p.Center), Radius = p.Radius, StartAngle = p.StartAngle, Sweep = p.Sweep,
                EllipseRadiusY = p.EllipseRadiusY, EllipseRotation = p.EllipseRotation }).ToList()).ToList()
        }).ToList()
    };
    public static void Restore(CadDocument document, CadRegionsSection section, CancellationToken token)
    {
        foreach (var data in section.Regions)
        {
            token.ThrowIfCancellationRequested();
            if (data.Contours.Count == 0 || data.Contours.Sum(c => (long)c.Count) > 32768)
                throw new InvalidDataException("Region boundary exceeds the geometry budget.");
            var e = data.Entity;
            var region = new CadRegion(new(e.Id), new(e.LayerId), new(e.OwnerBlockId),
                data.Contours.Select(c => new CadRegionContour(c.Select(p => new CadPlanarPrimitive(Point(p.Start), Point(p.End), Point(p.Center), p.Radius, p.StartAngle, p.Sweep, p.EllipseRadiusY, p.EllipseRotation)))), e.Name);
            region.SetGraphicStyleInternal(data.GraphicStyleId is { } graphic ? new StyleId(graphic) : null);
            region.SetFillStyleInternal(data.FillStyleId is { } fill ? new StyleId(fill) : null);
            CadDocumentMapper.ApplyEntityState(document, region, e);
            document.AddEntityCore(region);
        }
    }
}
