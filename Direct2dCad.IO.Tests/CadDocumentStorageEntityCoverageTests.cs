using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Container;

namespace Direct2dCad.IO.Tests;

public sealed class CadDocumentStorageEntityCoverageTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsEveryGeometryEntityType()
    {
        var path = CreateTempPath();
        try
        {
            var document = CadDocument.Create("All entities");
            var fillStyleId = document.CreateSolidFillStyle("Solid", CadColor.Green);
            var line = document.AddLine(new CadPointD(1, 2), new CadPointD(3, 4), name: "Line");
            var circle = document.AddCircle(new CadPointD(10, 20), 5, fillStyleId: fillStyleId, name: "Circle");
            var ellipse = document.AddEllipse(new CadPointD(20, 30), 8, 3, fillStyleId: fillStyleId, name: "Ellipse");
            var ellipseArc = document.AddEllipseArc(new CadPointD(30, 40), 9, 4, 0.2, 1.7, name: "EllipseArc");
            var arc = document.AddArc(new CadPointD(40, 50), 7, 0.5, -2.1, name: "Arc");
            var rectangle = document.AddRectangle(
                CadRectD.FromXYWH(50, 60, 12, 8),
                1.5,
                2.5,
                fillStyleId: fillStyleId,
                name: "Rectangle");
            var polyline = document.AddPolyline(
                [new CadPointD(0, 0), new CadPointD(5, 1), new CadPointD(4, 6)],
                isClosed: true,
                fillStyleId: fillStyleId,
                name: "Polyline");
            var spline = document.AddSpline(
                [new CadPointD(0, 0), new CadPointD(3, 8), new CadPointD(10, 2)],
                closed: true,
                fillStyleId: fillStyleId,
                name: "Spline");
            var text = document.AddText(
                "日本語 Text",
                new CadPointD(70, 80),
                6,
                rotationRadians: 0.3,
                name: "Text",
                isInverted: true,
                invertedMarginFactor: 0.2);
            text.SetLocalBounds(CadRectD.FromLTRB(0, -1, 25, 6));
            var shapeText = document.AddShapeText(
                "Shape",
                new CadPointD(90, 100),
                5,
                rotationRadians: 0.4,
                widthFactor: 1.2,
                characterSpacingFactor: 0.15,
                obliqueAngleRadians: 0.1,
                name: "ShapeText",
                isInverted: true,
                invertedMarginFactor: 0.3);
            line.SetLineWeight(new CadLineWeight(0.7));
            line.SetZIndex(12);
            line.SetStrokeStyle(new CadStrokeStyle(
                CadStrokeCap.Round,
                CadStrokeCap.Square,
                CadStrokeCap.Triangle,
                CadStrokeDashStyle.DashDot,
                CadStrokeLineJoin.Bevel));

            var storage = new CadDocumentStorage();
            await storage.SaveAsync(document, path);
            var loaded = await storage.LoadAsync(path);

            var loadedLine = Assert.IsType<CadLine>(loaded.GetEntity(line.Id));
            Assert.Equal(line.Start, loadedLine.Start);
            Assert.Equal(line.End, loadedLine.End);
            Assert.Equal(line.LineWeight, loadedLine.LineWeight);
            Assert.Equal(line.StrokeStyle, loadedLine.StrokeStyle);
            Assert.Equal(12, loadedLine.ZIndex);

            Assert.Equal(circle.Radius, Assert.IsType<CadCircle>(loaded.GetEntity(circle.Id)).Radius);
            Assert.Equal(ellipse.RadiusY, Assert.IsType<CadEllipse>(loaded.GetEntity(ellipse.Id)).RadiusY);
            Assert.Equal(ellipseArc.SweepAngleRadians, Assert.IsType<CadEllipseArc>(loaded.GetEntity(ellipseArc.Id)).SweepAngleRadians);
            Assert.Equal(arc.SweepAngleRadians, Assert.IsType<CadArc>(loaded.GetEntity(arc.Id)).SweepAngleRadians);

            var loadedRectangle = Assert.IsType<CadRectangle>(loaded.GetEntity(rectangle.Id));
            Assert.Equal(rectangle.CornerRadiusX, loadedRectangle.CornerRadiusX);
            Assert.Equal(rectangle.CornerRadiusY, loadedRectangle.CornerRadiusY);
            Assert.Equal(fillStyleId, loadedRectangle.FillStyleId);

            Assert.Equal(polyline.Points, Assert.IsType<CadPolyline>(loaded.GetEntity(polyline.Id)).Points);
            Assert.True(Assert.IsType<CadPolyline>(loaded.GetEntity(polyline.Id)).Closed);
            Assert.Equal(spline.FitPoints, Assert.IsType<CadSpline>(loaded.GetEntity(spline.Id)).FitPoints);
            Assert.True(Assert.IsType<CadSpline>(loaded.GetEntity(spline.Id)).Closed);

            var loadedText = Assert.IsType<CadText>(loaded.GetEntity(text.Id));
            Assert.Equal(text.Text, loadedText.Text);
            Assert.Equal(text.LocalBounds, loadedText.LocalBounds);
            Assert.Equal(text.IsInverted, loadedText.IsInverted);
            Assert.Equal(text.RotationRadians, loadedText.RotationRadians);

            var loadedShapeText = Assert.IsType<CadShapeText>(loaded.GetEntity(shapeText.Id));
            Assert.Equal(shapeText.Text, loadedShapeText.Text);
            Assert.Equal(shapeText.WidthFactor, loadedShapeText.WidthFactor);
            Assert.Equal(shapeText.CharacterSpacingFactor, loadedShapeText.CharacterSpacingFactor);
            Assert.Equal(shapeText.ShapeFontId, loadedShapeText.ShapeFontId);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task LoadAsync_DuplicateSectionKindThrowsInvalidDataException()
    {
        var path = CreateTempPath();
        try
        {
            var storage = new CadDocumentStorage();
            await storage.SaveAsync(CadDocument.Create("Duplicate"), path);

            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 25 + 19;
                await stream.WriteAsync(new byte[] { (byte)CadSectionKind.Document, 0 });
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await storage.LoadAsync(path));
            Assert.Contains("Duplicate section", exception.Message);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task LoadAsync_UnknownSectionVersionThrowsNotSupportedException()
    {
        var path = CreateTempPath();
        try
        {
            var storage = new CadDocumentStorage();
            await storage.SaveAsync(CadDocument.Create("Version"), path);

            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 25 + 2;
                await stream.WriteAsync(BitConverter.GetBytes(999));
            }

            await Assert.ThrowsAsync<NotSupportedException>(
                async () => await storage.LoadAsync(path));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task SaveAsync_WithPreCanceledTokenDoesNotWriteFile()
    {
        var path = CreateTempPath();
        try
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var storage = new CadDocumentStorage();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await storage.SaveAsync(
                    CadDocument.Create("Canceled"),
                    path,
                    cancellation.Token));

            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"Direct2dCad-{Guid.NewGuid():N}.d2cad");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
