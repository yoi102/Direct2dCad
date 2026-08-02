using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class TransformEntitiesCommandTests
{
    [Fact]
    public void RotatePolyline_ThenUndo_RestoresEveryPoint()
    {
        var document = CadDocument.Create("Test");
        var original = new[]
        {
            new CadPointD(1, 0),
            new CadPointD(2, 0),
            new CadPointD(2, 1)
        };
        var polyline = document.AddPolyline(original, isClosed: false);
        var command = new RotateEntitiesCommand([polyline.Id], CadPointD.Origin, Math.PI * 0.5);

        command.Execute(document);

        AssertPoint(new CadPointD(0, 1), polyline.Points[0]);
        AssertPoint(new CadPointD(0, 2), polyline.Points[1]);
        AssertPoint(new CadPointD(-1, 2), polyline.Points[2]);

        command.Undo(document);

        for (var index = 0; index < original.Length; index++)
            AssertPoint(original[index], polyline.Points[index]);
    }

    [Fact]
    public void ScaleCircle_ThenUndo_RestoresCenterAndRadius()
    {
        var document = CadDocument.Create("Test");
        var circle = document.AddCircle(new CadPointD(4, 6), 3);
        var command = new ScaleEntitiesCommand([circle.Id], new CadPointD(1, 2), 2.5);

        command.Execute(document);

        AssertPoint(new CadPointD(8.5, 12), circle.Center);
        Assert.Equal(7.5, circle.Radius, 10);

        command.Undo(document);

        AssertPoint(new CadPointD(4, 6), circle.Center);
        Assert.Equal(3, circle.Radius, 10);
    }

    [Fact]
    public void MirrorArc_ThenUndo_RestoresAnglesAndCenter()
    {
        var document = CadDocument.Create("Test");
        var arc = document.AddArc(new CadPointD(3, 4), 5, Math.PI / 6, Math.PI / 2);
        var command = new MirrorEntitiesCommand([arc.Id], CadPointD.Origin, 0);

        command.Execute(document);

        AssertPoint(new CadPointD(3, -4), arc.Center);
        Assert.Equal(-Math.PI / 6, arc.StartAngleRadians, 10);
        Assert.Equal(-Math.PI / 2, arc.SweepAngleRadians, 10);

        command.Undo(document);

        AssertPoint(new CadPointD(3, 4), arc.Center);
        Assert.Equal(Math.PI / 6, arc.StartAngleRadians, 10);
        Assert.Equal(Math.PI / 2, arc.SweepAngleRadians, 10);
    }

    [Fact]
    public void RotateRoundedRectangle_QuarterTurn_SwapsCornerRadiiAndUndoRestoresThem()
    {
        var document = CadDocument.Create("Test");
        var rectangle = document.AddRectangle(CadRectD.FromLTRB(0, 0, 8, 4), 3, 1);
        var command = new RotateEntitiesCommand([rectangle.Id], CadPointD.Origin, Math.PI * 0.5);

        command.Execute(document);

        Assert.Equal(1, rectangle.CornerRadiusX, 10);
        Assert.Equal(3, rectangle.CornerRadiusY, 10);
        AssertRect(CadRectD.FromLTRB(-4, 0, 0, 8), rectangle.Bounds);

        command.Undo(document);

        Assert.Equal(3, rectangle.CornerRadiusX, 10);
        Assert.Equal(1, rectangle.CornerRadiusY, 10);
        AssertRect(CadRectD.FromLTRB(0, 0, 8, 4), rectangle.Bounds);
    }

    [Fact]
    public void MultiEntityRotation_ValidatesAllEntitiesBeforeMutatingAny()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(new CadPointD(1, 0), new CadPointD(2, 0));
        var ellipse = document.AddEllipse(new CadPointD(5, 5), 3, 2);
        var command = new RotateEntitiesCommand([line.Id, ellipse.Id], CadPointD.Origin, Math.PI / 3);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));

        Assert.Equal(new CadPointD(1, 0), line.Start);
        Assert.Equal(new CadPointD(2, 0), line.End);
        Assert.Equal(new CadPointD(5, 5), ellipse.Center);
        Assert.Equal(3, ellipse.RadiusX);
        Assert.Equal(2, ellipse.RadiusY);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    public void AxisAlignedEntities_RejectUnsupportedArbitraryRotation(double degrees)
    {
        var document = CadDocument.Create("Test");
        var rectangle = document.AddRectangle(CadRectD.FromLTRB(0, 0, 10, 5));
        var ellipse = document.AddEllipse(CadPointD.Origin, 4, 2);
        var command = new RotateEntitiesCommand(
            [rectangle.Id, ellipse.Id],
            CadPointD.Origin,
            degrees * Math.PI / 180.0);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Equal(CadRectD.FromLTRB(0, 0, 10, 5), rectangle.Bounds);
        Assert.Equal(CadPointD.Origin, ellipse.Center);
    }

    [Fact]
    public void EllipseArc_RejectsScaleBeforeMutatingGeometry()
    {
        var document = CadDocument.Create("Test");
        var ellipseArc = document.AddEllipseArc(CadPointD.Origin, 8, 4, 0.2, 1.1);
        var originalBounds = ellipseArc.Bounds;
        var command = new ScaleEntitiesCommand([ellipseArc.Id], CadPointD.Origin, 2);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Equal(originalBounds, ellipseArc.Bounds);
        Assert.Equal(8, ellipseArc.RadiusX, 10);
        Assert.Equal(4, ellipseArc.RadiusY, 10);
    }

    [Fact]
    public void OleObject_RejectsRotationBeforeMutatingBounds()
    {
        var document = CadDocument.Create("Test");
        var ole = document.AddOleObject(CadRectD.FromXYWH(10, 20, 8, 4), [1, 2, 3]);
        var originalBounds = ole.Bounds;
        var command = new RotateEntitiesCommand([ole.Id], CadPointD.Origin, Math.PI / 2);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Equal(originalBounds, ole.Bounds);
    }

    [Theory]
    [InlineData(22.5)]
    [InlineData(67.5)]
    public void Mirror_RejectsUnsupportedAxisForEllipseAndRectangle(double degrees)
    {
        var document = CadDocument.Create("Test");
        var ellipse = document.AddEllipse(CadPointD.Origin, 6, 3);
        var rectangle = document.AddRectangle(CadRectD.FromLTRB(10, 10, 20, 15));
        var ellipseBounds = ellipse.Bounds;
        var rectangleBounds = rectangle.Bounds;
        var command = new MirrorEntitiesCommand(
            [ellipse.Id, rectangle.Id],
            CadPointD.Origin,
            degrees * Math.PI / 180.0);

        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Equal(ellipseBounds, ellipse.Bounds);
        Assert.Equal(rectangleBounds, rectangle.Bounds);
    }

    [Fact]
    public void EllipseArc_RejectsMirrorBeforeMutatingGeometry()
    {
        var document = CadDocument.Create("Test");
        var ellipseArc = document.AddEllipseArc(new CadPointD(2, 3), 8, 4, 0, Math.PI / 2);
        var originalBounds = ellipseArc.Bounds;

        Assert.Throws<NotSupportedException>(() => new MirrorEntitiesCommand(
            [ellipseArc.Id], CadPointD.Origin, 0).Execute(document));

        Assert.Equal(originalBounds, ellipseArc.Bounds);
    }

    private static void AssertPoint(CadPointD expected, CadPointD actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    private static void AssertRect(CadRectD expected, CadRectD actual)
    {
        Assert.Equal(expected.MinX, actual.MinX, 9);
        Assert.Equal(expected.MinY, actual.MinY, 9);
        Assert.Equal(expected.MaxX, actual.MaxX, 9);
        Assert.Equal(expected.MaxY, actual.MaxY, 9);
    }
}
