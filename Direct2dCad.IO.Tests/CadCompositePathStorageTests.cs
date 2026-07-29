using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.IO.Tests;

public sealed class CadCompositePathStorageTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsMixedCompositePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.d2cad");
        try
        {
            var document = CadDocument.Create("Composite");
            var fill = document.CreateSolidFillStyle("Fill", CadColor.Green);
            var entity = document.AddCompositePath(
                new CadPointD(1, 2),
                [
                    new CadCompositeLineSegment(new CadPointD(11, 2)),
                    new CadCompositeArcSegment(new CadPointD(11, 7), -Math.PI / 2),
                    new CadCompositeBezierSegment(
                        new CadPointD(13, 10),
                        new CadPointD(17, 10),
                        new CadPointD(18, 8)),
                    new CadCompositeSplineSegment([new CadPointD(14, 8), new CadPointD(20, 2)])
                ],
                closed: true,
                fillStyleId: fill,
                name: "MixedPath");

            var storage = new CadDocumentStorage();
            await storage.SaveAsync(document, path);
            var loaded = await storage.LoadAsync(path);

            var actual = Assert.IsType<CadCompositePath>(loaded.GetEntity(entity.Id));
            Assert.Equal(entity.StartPoint, actual.StartPoint);
            Assert.Equal(entity.Closed, actual.Closed);
            Assert.Equal(entity.FillStyleId, actual.FillStyleId);
            Assert.Equal(4, actual.Segments.Count);
            Assert.IsType<CadCompositeLineSegment>(actual.Segments[0]);
            Assert.IsType<CadCompositeArcSegment>(actual.Segments[1]);
            var bezier = Assert.IsType<CadCompositeBezierSegment>(actual.Segments[2]);
            Assert.Equal(new CadPointD(13, 10), bezier.Control1);
            Assert.Equal(new CadPointD(17, 10), bezier.Control2);
            Assert.Equal(new CadPointD(18, 8), bezier.End);
            Assert.IsType<CadCompositeSplineSegment>(actual.Segments[3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
