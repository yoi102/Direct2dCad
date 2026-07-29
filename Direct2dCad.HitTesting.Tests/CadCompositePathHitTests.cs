using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting.Tests;

public sealed class CadCompositePathHitTests
{
    [Fact]
    public void HitTesting_CoversLineArcSplineAndClosedFill()
    {
        var document = CadDocument.Create("Hit");
        var fill = document.CreateSolidFillStyle("Fill", CadColor.Green);
        var path = document.AddCompositePath(
            CadPointD.Origin,
            [
                new CadCompositeLineSegment(new CadPointD(10, 0)),
                new CadCompositeArcSegment(new CadPointD(10, 5), Math.PI / 2),
                new CadCompositeSplineSegment([new CadPointD(10, 10), new CadPointD(0, 10)])
            ],
            closed: true,
            fillStyleId: fill);

        Assert.True(CadEntityHitTester.HitTestEdge(document, path, new CadPointD(5, 0), 0.05, out _));
        Assert.True(CadEntityHitTester.HitTestEdge(document, path, new CadPointD(15, 5), 0.1, out _));
        Assert.True(CadEntityHitTester.HitTestEdge(document, path, new CadPointD(5, 10), 0.2, out _));
        Assert.True(CadEntityHitTester.HitTestFill(document, path, new CadPointD(5, 5), out _));
    }

    [Fact]
    public void HitTesting_CoversCubicBezierEdgeAndClosedFill()
    {
        var document = CadDocument.Create("BezierHit");
        var fill = document.CreateSolidFillStyle("Fill", CadColor.Green);
        var path = document.AddCompositePath(
            CadPointD.Origin,
            [
                new CadCompositeBezierSegment(
                    new CadPointD(0, 10),
                    new CadPointD(10, 10),
                    new CadPointD(10, 0)),
                new CadCompositeLineSegment(CadPointD.Origin)
            ],
            closed: true,
            fillStyleId: fill);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            path,
            new CadPointD(5, 7.5),
            0.05,
            out _));
        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            path,
            new CadPointD(5, 3),
            out _));
    }
}
