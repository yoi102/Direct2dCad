using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Commands;
using Direct2dCad.Editor;
using Direct2dCad.HitTesting;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.wpf.Services.Printing.Vector;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class EllipticalRegionRenderingTests
{
    private const int Width = 640, Height = 480;
    private const double Rotation = 44 * Math.PI / 180;
    private static readonly CadPointD Center = new(12, -7);
    private static readonly CadColor Highlight = CadColor.FromArgb(255, 255, 214, 92);

    public static IEnumerable<object[]> Cases()
    {
        foreach (var shape in new[] { "ellipse", "ring", "half" })
        foreach (var background in new[] { false, true })
        foreach (var path in new[] { "selection", "translated", "uncached-selection", "uncached-preview", "primitive-preview" })
            yield return [shape, background, path];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "WindowsIntegration")]
    public void ExactEllipticalRegionAgreesAcrossPixelsHitTestingAndVectorPrinting(
        string shape, bool background, string path)
    {
        var document = CadDocument.Create("elliptical region");
        var fill = document.CreateSolidFillStyle("green", CadColor.Green);
        var outer = Ellipse(60, 20, 0, shape == "half" ? Math.PI : Math.Tau);
        var contours = shape == "ring"
            ? new[] { new CadRegionContour([outer]), new CadRegionContour([Ellipse(24, 8, 0, -Math.Tau)]) }
            : new[] { new CadRegionContour(shape == "half"
                ? [outer, CadPlanarPrimitive.Line(outer.End, outer.Start)] : [outer]) };
        var region = document.AddRegion(contours, fillStyleId: fill);
        var points = contours.SelectMany(c => c.Edges).SelectMany(edge =>
            new[] { .15, .35, .65, .85 }.Select(edge.At)).ToArray();

        var printed = CadVectorPrintGeometryFactory.Create(region)!;
        Assert.Equal(FillRule.EvenOdd, PathGeometry.CreateFromGeometry(printed).FillRule);
        Assert.All(PathGeometry.CreateFromGeometry(printed).Figures.SelectMany(f => f.Segments), segment =>
            Assert.True(segment is ArcSegment or LineSegment or PolyLineSegment,
                "Region printing must retain vector arcs and lines."));
        Assert.Contains(PathGeometry.CreateFromGeometry(printed).Figures.SelectMany(f => f.Segments),
            segment => segment is ArcSegment arc && Math.Abs(arc.Size.Width - arc.Size.Height) > 1);
        foreach (var point in points)
        {
            Assert.True(CadEntityHitTester.HitTestEdge(document, region, point, .01, out _));
            Assert.True(printed.StrokeContains(new Pen(Brushes.Black, .15), CadVectorPrintGeometryFactory.ToPoint(point)),
                "Printed geometry must pass through the exact ellipse edge.");
        }

        var material = LocalPoint(shape == "half" ? 0 : 40, shape == "half" ? 8 : 0);
        Assert.True(CadEntityHitTester.HitTestFill(document, region, material, out _));
        Assert.True(printed.FillContains(CadVectorPrintGeometryFactory.ToPoint(material)));
        if (shape == "ring")
        {
            Assert.False(CadEntityHitTester.HitTestFill(document, region, Center, out _));
            Assert.False(printed.FillContains(CadVectorPrintGeometryFactory.ToPoint(Center)));
        }

        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(2, new(Width / 2, Height / 2));
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsLevelOfDetailEnabled = false, EnableGeometryRealizations = true
        });
        host.SetScene(document, viewport, prepareResourcesInBackground: background);
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (host.PrepareRenderCacheStep())
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, "Region resources must become ready.");
            Thread.Yield();
        }
        host.Render(CadRenderInvalidation.Full);
        var ordinary = host.CaptureBackBufferPixels();
        Assert.All(points, p => Assert.True(HasPixel(ordinary, viewport, p, false)));
        Assert.True(HasPixel(ordinary, viewport, material, false));
        if (shape == "ring") Assert.False(HasPixel(ordinary, viewport, Center, false));

        var offset = path is "translated" or "uncached-preview" or "primitive-preview" ? new CadVectorD(25, -12) : CadVectorD.Zero;
        if (path.StartsWith("uncached", StringComparison.Ordinal)) host.RemoveEntity(region.Id);
        if (path.EndsWith("selection", StringComparison.Ordinal))
        {
            var scene = new CadHandleScene();
            scene.Replace([new CadSelectionEntityReference(region.Id, region.Bounds, offset,
                new(Highlight, CadColor.Transparent, Size: 0, StrokeWidth: 2))]);
            host.SetHandleScene(scene);
        }
        else
        {
            var scene = new CadTransientScene();
            var style = new CadTransientStyle(Highlight, StrokeWidth: 2);
            if (path == "primitive-preview")
            {
                scene.Replace(contours.SelectMany(c => c.Edges).Select(edge => edge.IsLine
                    ? (CadTransientItem)new CadTransientLine(edge.Start + offset, edge.End + offset, style)
                    : new CadTransientEllipseArc(edge.Center + offset, edge.RadiusX, edge.RadiusY,
                        edge.StartAngle, edge.Sweep, style, edge.Rotation)).ToArray());
            }
            else
                scene.Replace([new CadTransientEntityReference(region.Id, offset, style,
                    UseSourceAppearance: path == "uncached-preview")]);
            host.SetTransientScene(scene);
        }
        host.Render(CadRenderInvalidation.Full);
        var highlighted = host.CaptureBackBufferPixels();
        SaveEvidence(highlighted, $"{shape}-{path}-{(background ? "background" : "synchronous")}");
        Assert.All(points, p => Assert.True(HasPixel(highlighted, viewport, p + offset, true),
            "Every selected or preview edge must retain its exact ellipse, rotation and translation."));
        var circularImpostor = LocalPoint(60 * Math.Cos(.7), 60 * Math.Sin(.7));
        Assert.False(HasPixel(highlighted, viewport, circularImpostor + offset, true),
            "An elliptical region edge must never render as a circular arc.");
    }

    private static CadPlanarPrimitive Ellipse(double radiusX, double radiusY, double start, double sweep)
    {
        CadPointD At(double angle) => LocalPoint(radiusX * Math.Cos(angle), radiusY * Math.Sin(angle));
        return new(At(start), At(start + sweep), Center, radiusX, start, sweep, radiusY, Rotation);
    }

    [Theory]
    [InlineData(CadBooleanOperation.Union)]
    [InlineData(CadBooleanOperation.Intersection)]
    [InlineData(CadBooleanOperation.Difference)]
    [Trait("Category", "WindowsIntegration")]
    public void BooleanEllipseAndExistingRegionKeepExactVisibleEdgesThroughUndoRedo(CadBooleanOperation operation)
    {
        var document = CadDocument.Create("ellipse plus region boolean");
        var ellipse = document.AddEllipse(Center, 60, 20);
        ellipse.SetRotation(Rotation);
        var other = document.AddRegion([new CadRegionContour([CadPlanarPrimitive.Arc(new(40, 8), 24, 0, Math.Tau)])]);
        var editor = new CadEditor(document);
        var viewport = editor.Viewport;
        viewport.SetSize(Width, Height);
        viewport.SetView(2, new(Width / 2, Height / 2));
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        editor.RegisterGeometryResourceManager(host, rebuildExistingResources: false);
        host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false, IsLevelOfDetailEnabled = false });
        host.Render(CadRenderInvalidation.Full);
        var before = host.CaptureBackBufferPixels();
        var command = new BooleanRegionsCommand([ellipse.Id, other.Id], operation, ellipse.Id);
        editor.Execute(command);
        Assert.NotNull(command.ResultEntityId);
        Assert.True(document.TryGetEntity(command.ResultEntityId!.Value, out var result));
        var region = Assert.IsType<CadRegion>(result);
        Assert.Contains(region.Contours.SelectMany(c => c.Edges), edge => edge.IsEllipse);
        Assert.True(ellipse.IsErased);
        Assert.True(other.IsErased);
        host.Render(CadRenderInvalidation.Full);
        var after = host.CaptureBackBufferPixels();
        SaveEvidence(after, $"boolean-ellipse-region-{operation}");
        var print = CadVectorPrintGeometryFactory.Create(region)!;
        foreach (var edge in region.Contours.SelectMany(c => c.Edges))
        foreach (var t in new[] { .25, .5, .75 })
        {
            var point = edge.At(t);
            Assert.True(HasPixel(after, viewport, point, false));
            Assert.True(CadEntityHitTester.HitTestEdge(document, region, point, .01, out _));
            Assert.True(print.StrokeContains(new Pen(Brushes.Black, .15), CadVectorPrintGeometryFactory.ToPoint(point)));
        }
        editor.Undo();
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(before, host.CaptureBackBufferPixels());
        editor.Redo();
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(after, host.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "WindowsIntegration")]
    public void RotatedEllipseArcPreviewPartialFramesMatchFullDrawAndLeaveNoTrails(bool grouped)
    {
        var document = CadDocument.Create("ellipse preview invalidation");
        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(2, new(Width / 2, Height / 2));
        var style = new CadTransientStyle(Highlight, StrokeWidth: 2);
        var scene = new CadTransientScene();
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetTransientScene(scene);
        host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        host.Render(CadRenderInvalidation.Full);
        var empty = host.CaptureBackBufferPixels();
        var calculator = new CadRenderInvalidationCalculator(document, viewport, Width, Height, _ => style);
        var previous = CadRenderInvalidation.Empty;
        foreach (var offset in new[] { -45, 45, 0 })
        {
            CadTransientItem item = new CadTransientEllipseArc(new(offset, 0), 80, 8, 0, Math.Tau, style, Math.PI / 2);
            if (grouped) item = new CadTransientGroup([item], CadMatrixD.CreateTranslation(0, 5));
            scene.Replace([item]);
            var current = calculator.CreateTransientSceneInvalidation(scene);
            host.Render(previous.UnionPreservingCoverage(current), baseSceneChanged: false);
            var partial = host.CaptureBackBufferPixels();
            Assert.True(HasPixel(partial, viewport, new(offset, 80 + (grouped ? 5 : 0)), true));
            host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
            Assert.Equal(host.CaptureBackBufferPixels(), partial);
            previous = current;
        }
        scene.Clear();
        host.Render(previous, baseSceneChanged: false);
        Assert.Equal(empty, host.CaptureBackBufferPixels());
    }

    private static CadPointD LocalPoint(double x, double y) =>
        CadMatrixD.CreateRotation(Rotation, Center).TransformPoint(Center + new CadVectorD(x, y));

    private static bool HasPixel(byte[] pixels, CadViewport viewport, CadPointD world, bool highlightOnly)
    {
        var screen = viewport.WorldToScreen(world);
        for (var y = Math.Max(0, (int)Math.Round(screen.Y) - 2); y <= Math.Min(Height - 1, (int)Math.Round(screen.Y) + 2); y++)
        for (var x = Math.Max(0, (int)Math.Round(screen.X) - 2); x <= Math.Min(Width - 1, (int)Math.Round(screen.X) + 2); x++)
        {
            var i = (y * Width + x) * 4;
            if (highlightOnly ? pixels[i + 2] > 150 && pixels[i + 1] > 100 && pixels[i] < 140
                : pixels[i] > 40 || pixels[i + 1] > 40 || pixels[i + 2] > 40) return true;
        }
        return false;
    }

    private static void SaveEvidence(byte[] pixels, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_ELLIPSE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var image = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = Width;
        public int SurfaceHeight { get; private set; } = Height;
        public void SetSize(int width, int height) { SurfaceWidth = width; SurfaceHeight = height; }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
    }
}
