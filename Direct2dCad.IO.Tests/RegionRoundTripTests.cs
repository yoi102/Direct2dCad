using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;

namespace Direct2dCad.IO.Tests;

public sealed class RegionRoundTripTests
{
    [Fact]
    public async Task FileAndSnapshotKeepEveryContourAndExactArc()
    {
        var d = CadDocument.Create("region"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference));
        var block = d.CreateBlockDefinition("region-block", default); d.MoveEntityToBlock(r.Id, block);
        var reference = d.AddBlockReference(block, new(100, 0)); d.RefreshBlockReferenceBounds();
        var storage = new CadDocumentStorage(); var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            await storage.SaveAsync(d, path); var loaded = await storage.LoadAsync(path);
            var copy = Assert.IsType<CadRegion>(loaded.GetEntity(r.Id));
            Assert.Equal(r.Area, copy.Area, 9); Assert.Equal(r.OwnerBlockId, copy.OwnerBlockId);
            Assert.Equal(r.Contours.SelectMany(c => c.Edges), copy.Contours.SelectMany(c => c.Edges));
            Assert.Equal(reference.Bounds, loaded.GetEntity(reference.Id).Bounds);
            var snapshot = await storage.CreateIndependentSnapshotAsync(d, new(() => true, ct => ValueTask.CompletedTask));
            r.ReplaceGeometry(r.Contours.Select(c => c.Transform(p => p + new CadVectorD(20, 0))));
            Assert.False(((CadRegion)snapshot.GetEntity(r.Id)).Contains(default));
            Assert.True(((CadRegion)snapshot.GetEntity(r.Id)).Contains(new(5, 0)));
            Assert.True(new CadDxfStorage().AnalyzeExport(loaded).ContainsKey(nameof(CadRegion)));
        }
        finally { File.Delete(path); }
    }
}
