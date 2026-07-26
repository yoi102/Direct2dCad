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
            Assert.Equal(3, actual.Segments.Count);
            Assert.IsType<CadCompositeLineSegment>(actual.Segments[0]);
            Assert.IsType<CadCompositeArcSegment>(actual.Segments[1]);
            Assert.IsType<CadCompositeSplineSegment>(actual.Segments[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
