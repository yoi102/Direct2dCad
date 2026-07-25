using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class AddEntityCommandCoverageTests
{
    [Fact]
    public void AddCurveCommands_UndoAndRedoPreserveEntityIdentityAndProperties()
    {
        var points = new[]
        {
            new CadPointD(1, 2),
            new CadPointD(7, 3),
            new CadPointD(5, 9)
        };
        var lineWeight = new CadLineWeight(2.5);

        var cases = new AddCommandCase[]
        {
            new(
                new AddCircleCommand(
                    new CadPointD(4, 5),
                    3,
                    name: "Circle1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var circle = Assert.IsType<CadCircle>(entity);
                    Assert.Equal(new CadPointD(4, 5), circle.Center);
                    Assert.Equal(3, circle.Radius);
                }),
            new(
                new AddEllipseCommand(
                    new CadPointD(6, 8),
                    4,
                    2,
                    name: "Ellipse1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var ellipse = Assert.IsType<CadEllipse>(entity);
                    Assert.Equal(new CadPointD(6, 8), ellipse.Center);
                    Assert.Equal(4, ellipse.RadiusX);
                    Assert.Equal(2, ellipse.RadiusY);
                }),
            new(
                new AddEllipseArcCommand(
                    new CadPointD(2, 3),
                    5,
                    2,
                    0.25,
                    1.75,
                    name: "EllipseArc1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var arc = Assert.IsType<CadEllipseArc>(entity);
                    Assert.Equal(5, arc.RadiusX);
                    Assert.Equal(2, arc.RadiusY);
                    Assert.Equal(0.25, arc.StartAngleRadians);
                    Assert.Equal(1.75, arc.SweepAngleRadians);
                }),
            new(
                new AddArcCommand(
                    new CadPointD(3, 4),
                    6,
                    0.5,
                    -1.25,
                    name: "Arc1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var arc = Assert.IsType<CadArc>(entity);
                    Assert.Equal(6, arc.Radius);
                    Assert.Equal(0.5, arc.StartAngleRadians);
                    Assert.Equal(-1.25, arc.SweepAngleRadians);
                }),
            new(
                new AddRectangleCommand(
                    CadRectD.FromLTRB(1, 2, 11, 8),
                    1.5,
                    0.75,
                    name: "Rectangle1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var rectangle = Assert.IsType<CadRectangle>(entity);
                    Assert.Equal(CadRectD.FromLTRB(1, 2, 11, 8), rectangle.Bounds);
                    Assert.Equal(1.5, rectangle.CornerRadiusX);
                    Assert.Equal(0.75, rectangle.CornerRadiusY);
                }),
            new(
                new AddPolylineCommand(
                    points,
                    closed: true,
                    name: "Polyline1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var polyline = Assert.IsType<CadPolyline>(entity);
                    Assert.Equal(points, polyline.Points);
                    Assert.True(polyline.Closed);
                }),
            new(
                new AddPolygonCommand(
                    points,
                    name: "Polygon1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var polygon = Assert.IsType<CadPolyline>(entity);
                    Assert.Equal(points, polygon.Points);
                    Assert.True(polygon.Closed);
                }),
            new(
                new AddSplineCommand(
                    points,
                    closed: true,
                    name: "Spline1",
                    lineWeight: lineWeight,
                    zIndex: 7,
                    isVisible: false),
                entity =>
                {
                    var spline = Assert.IsType<CadSpline>(entity);
                    Assert.Equal(points, spline.FitPoints);
                    Assert.True(spline.Closed);
                })
        };

        foreach (var testCase in cases)
        {
            var document = CadDocument.Create("Test");
            var entity = ExecuteUndoRedo(document, testCase.Command);

            Assert.Equal(lineWeight, entity.LineWeight);
            Assert.Equal(7, entity.ZIndex);
            Assert.False(entity.IsVisible);
            testCase.AssertEntity(entity);
        }
    }

    [Fact]
    public void AddTextCommands_UndoAndRedoPreserveTextGeometry()
    {
        var textDocument = CadDocument.Create("Text");
        var textCommand = new AddTextCommand(
            "CAD text",
            new CadPointD(10, 20),
            4,
            rotationRadians: 0.75,
            name: "Text1",
            isInverted: true,
            invertedMarginFactor: 0.2,
            lineWeight: new CadLineWeight(1.25),
            zIndex: 6,
            isVisible: false);

        var text = Assert.IsType<CadText>(ExecuteUndoRedo(textDocument, textCommand));
        Assert.Equal("CAD text", text.Text);
        Assert.Equal(new CadPointD(10, 20), text.Position);
        Assert.Equal(4, text.Height);
        Assert.Equal(0.75, text.RotationRadians);
        Assert.True(text.IsInverted);
        Assert.Equal(0.2, text.InvertedMarginFactor);
        Assert.Equal(new CadLineWeight(1.25), text.LineWeight);
        Assert.Equal(6, text.ZIndex);
        Assert.False(text.IsVisible);

        var shapeDocument = CadDocument.Create("Shape text");
        var shapeCommand = new AddShapeTextCommand(
            "Shape",
            new CadPointD(3, 9),
            2.5,
            rotationRadians: 0.4,
            widthFactor: 1.2,
            characterSpacingFactor: 0.3,
            obliqueAngleRadians: 0.1,
            name: "ShapeText1",
            isInverted: true,
            invertedMarginFactor: 0.15);

        var shapeText = Assert.IsType<CadShapeText>(ExecuteUndoRedo(shapeDocument, shapeCommand));
        Assert.Equal("Shape", shapeText.Text);
        Assert.Equal(new CadPointD(3, 9), shapeText.Position);
        Assert.Equal(2.5, shapeText.Height);
        Assert.Equal(0.4, shapeText.RotationRadians);
        Assert.Equal(1.2, shapeText.WidthFactor);
        Assert.Equal(0.3, shapeText.CharacterSpacingFactor);
        Assert.Equal(0.1, shapeText.ObliqueAngleRadians);
        Assert.True(shapeText.IsInverted);
        Assert.Equal(0.15, shapeText.InvertedMarginFactor);
    }

    [Fact]
    public void AddRasterCommands_CloneInputDataAndPreserveDisplayProperties()
    {
        var pixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var imageDocument = CadDocument.Create("Image");
        var imageCommand = new AddImageCommand(
            CadRectD.FromLTRB(1, 2, 9, 6),
            pixelWidth: 2,
            pixelHeight: 2,
            stride: 8,
            pixels,
            contentType: "image/test",
            sourceName: "source.png",
            name: "Image1",
            zIndex: 8,
            isVisible: false,
            opacity: 0.35,
            rotationRadians: 0.5);
        pixels[0] = byte.MaxValue;

        var image = Assert.IsType<CadImage>(ExecuteUndoRedo(imageDocument, imageCommand));
        Assert.Equal(0, image.Pixels[0]);
        Assert.Equal("image/test", image.ContentType);
        Assert.Equal("source.png", image.SourceName);
        Assert.Equal(0.35, image.Opacity);
        Assert.Equal(0.5, image.RotationRadians);
        Assert.Equal(8, image.ZIndex);
        Assert.False(image.IsVisible);

        var oleBytes = new byte[] { 1, 2, 3, 4 };
        var oleDocument = CadDocument.Create("OLE");
        var oleCommand = new AddOleObjectCommand(
            CadRectD.FromLTRB(2, 4, 12, 14),
            oleBytes,
            contentType: "application/test",
            sourceName: "source.ole",
            name: "Ole1",
            zIndex: 9,
            isVisible: false,
            opacity: 0.6);
        oleBytes[0] = byte.MaxValue;

        var ole = Assert.IsType<CadOleObject>(ExecuteUndoRedo(oleDocument, oleCommand));
        Assert.Equal(1, ole.OleBytes[0]);
        Assert.Equal("application/test", ole.ContentType);
        Assert.Equal("source.ole", ole.SourceName);
        Assert.Equal(0.6, ole.Opacity);
        Assert.Equal(9, ole.ZIndex);
        Assert.False(ole.IsVisible);
    }

    [Fact]
    public void PointBasedAddCommands_RejectInvalidPointCounts()
    {
        var onePoint = new[] { CadPointD.Origin };
        var twoPoints = new[] { CadPointD.Origin, new CadPointD(1, 0) };

        Assert.Throws<ArgumentException>(() => new AddPolylineCommand(onePoint));
        Assert.Throws<ArgumentException>(() => new AddPolylineCommand(twoPoints, closed: true));
        Assert.Throws<ArgumentException>(() => new AddSplineCommand(onePoint));
        Assert.Throws<ArgumentException>(() => new AddSplineCommand(twoPoints, closed: true));
        Assert.Throws<ArgumentException>(() => new AddPolygonCommand(twoPoints));
    }

    private static CadEntity ExecuteUndoRedo(CadDocument document, ICadCommand command)
    {
        command.Execute(document);
        var entity = Assert.Single(document.Entities.Values);
        var id = entity.Id;
        Assert.False(entity.IsErased);

        command.Undo(document);
        Assert.True(entity.IsErased);

        command.Execute(document);
        Assert.Equal(id, entity.Id);
        Assert.Same(entity, document.GetEntity(id));
        Assert.False(entity.IsErased);
        return entity;
    }

    private sealed record AddCommandCase(ICadCommand Command, Action<CadEntity> AssertEntity);
}
