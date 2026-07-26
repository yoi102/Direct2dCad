using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting.Tests;

public sealed class AdditionalEntityHitTests
{
    [Fact]
    public void ArcEdge_HitsOnlyPointsInsideSweep()
    {
        var document = CadDocument.Create("Test");
        var arc = document.AddArcDegrees(
            CadPointD.Origin,
            radius: 10,
            startAngleDegrees: 0,
            sweepAngleDegrees: 90);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            arc,
            new CadPointD(0, 10),
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            arc,
            new CadPointD(-10, 0),
            0.01,
            out _));
    }

    [Fact]
    public void EllipseEdge_UsesBothRadii()
    {
        var document = CadDocument.Create("Test");
        var ellipse = document.AddEllipse(new CadPointD(5, 7), 10, 4);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            ellipse,
            new CadPointD(15, 7),
            0.01,
            out _));
        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            ellipse,
            new CadPointD(5, 11),
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            ellipse,
            new CadPointD(12, 11),
            0.01,
            out _));
    }

    [Fact]
    public void EllipseArcEdge_HitsOnlyPointsInsideSweep()
    {
        var document = CadDocument.Create("Test");
        var arc = document.AddEllipseArc(
            CadPointD.Origin,
            radiusX: 10,
            radiusY: 4,
            startAngleRadians: 0,
            sweepAngleRadians: Math.PI / 2);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            arc,
            new CadPointD(0, 4),
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            arc,
            new CadPointD(-10, 0),
            0.01,
            out _));
    }

    [Fact]
    public void RoundedRectangle_UsesRoundedEdgeAndFillStyle()
    {
        var document = CadDocument.Create("Test");
        var fillStyleId = document.CreateSolidFillStyle("Solid", CadColor.Green);
        var rectangle = document.AddRectangle(
            CadRectD.FromXYWH(0, 0, 20, 10),
            cornerRadiusX: 3,
            cornerRadiusY: 3,
            fillStyleId: fillStyleId);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            rectangle,
            new CadPointD(10, 10),
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            rectangle,
            new CadPointD(0, 0),
            0.01,
            out _));
        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            rectangle,
            new CadPointD(10, 5),
            out _));
        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            rectangle,
            new CadPointD(0, 0),
            out _));
    }

    [Fact]
    public void SplineEdge_UsesFlattenedCurveInsteadOfFitPointBounds()
    {
        var document = CadDocument.Create("Test");
        var spline = document.AddSpline(
        [
            new CadPointD(0, 0),
            new CadPointD(6, 8),
            new CadPointD(12, 0)
        ]);
        var curvePoint = spline.EnumerateFlattenedPoints(24).Skip(12).First();

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            spline,
            curvePoint,
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            spline,
            new CadPointD(6, -5),
            0.1,
            out _));
    }

    [Fact]
    public void ShapeTextEdge_HitsGeneratedStrokeOnly()
    {
        var document = CadDocument.Create("Test");
        var shapeText = document.AddShapeText("A", CadPointD.Origin, 10);
        var segment = shapeText.CreateStrokeSegments()[0];
        var midpoint = new CadPointD(
            (segment.Start.X + segment.End.X) * 0.5,
            (segment.Start.Y + segment.End.Y) * 0.5);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            shapeText,
            midpoint,
            0.01,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            shapeText,
            new CadPointD(shapeText.Bounds.MaxX + 5, shapeText.Bounds.MaxY + 5),
            0.1,
            out _));
    }

    [Fact]
    public void ClosedPolylineFill_HitsInteriorOnlyWhenFillStyleExists()
    {
        var document = CadDocument.Create("Test");
        var points = new[]
        {
            new CadPointD(0, 0),
            new CadPointD(10, 0),
            new CadPointD(10, 10),
            new CadPointD(0, 10)
        };
        var unfilled = document.AddPolyline(points, isClosed: true);
        var fillStyleId = document.CreateSolidFillStyle("Solid", CadColor.Green);
        var filled = document.AddPolyline(
            points.Select(point => point + new CadVectorD(20, 0)),
            isClosed: true,
            fillStyleId: fillStyleId);

        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            unfilled,
            new CadPointD(5, 5),
            out _));
        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            filled,
            new CadPointD(25, 5),
            out _));
        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            filled,
            new CadPointD(35, 5),
            out _));
    }

    [Fact]
    public void RotatedImageFillUsesFrameInsteadOfAxisAlignedBounds()
    {
        var document = CadDocument.Create("Test");
        var image = document.AddImage(
            CadRectD.FromXYWH(-5, -1, 10, 2),
            1,
            1,
            4,
            [1, 2, 3, 4],
            rotationRadians: Math.PI / 4);
        var axisAlignedCorner = new CadPointD(image.Bounds.MaxX, image.Bounds.MaxY);

        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            image,
            CadPointD.Origin,
            out _));
        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            image,
            axisAlignedCorner,
            out _));
    }

    [Fact]
    public void OleObjectFillHitsBoundsInterior()
    {
        var document = CadDocument.Create("Test");
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(10, 20, 30, 40),
            [1, 2, 3]);

        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            ole,
            new CadPointD(25, 35),
            out var result));
        Assert.Equal(ole.Id, result.LeafEntityId);
        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            ole,
            new CadPointD(5, 35),
            out _));
    }

    [Fact]
    public void BlockReferenceWithNegativeScaleReturnsReferenceAndLeafPath()
    {
        var document = CadDocument.Create("Test");
        var blockId = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        document.MoveEntityToBlock(line.Id, blockId);
        var reference = document.AddBlockReference(
            blockId,
            new CadPointD(100, 50),
            scaleX: -2,
            scaleY: 3);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            reference,
            new CadPointD(90, 50),
            0.1,
            out var result));
        Assert.Equal([reference.Id, line.Id], result.EntityPath);
    }
}
