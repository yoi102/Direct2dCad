using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class OrientedSelectionPixelTests
{
    private const int Width = 640, Height = 480;
    private static readonly CadColor Highlight = CadColor.FromArgb(255, 255, 214, 92);

    public static IEnumerable<object[]> Cases()
    {
        foreach (var geometryRealizations in new[] { false, true })
        foreach (var testCase in GeometryCases())
            yield return [.. testCase, geometryRealizations];
    }

    private static IEnumerable<object[]> GeometryCases()
    {
        foreach (var shape in new[] { "rectangle", "rounded", "hatch-rounded", "ellipse", "ellipse-arc" })
        foreach (var path in new[] { "selection", "hover", "translated", "uncached-preview" })
            yield return [shape, path, false];
        yield return ["rectangle", "selection", true];
        yield return ["rounded", "selection", true];
        yield return ["hatch-rounded", "selection", true];
        yield return ["rounded", "translated", true];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "WindowsIntegration")]
    public void OrientedOutlineMatchesActualGeometry(
        string shape, string path, bool mirroredBlock, bool geometryRealizations)
    {
        var document = CadDocument.Create("oriented selection");
        var center = new CadPointD(12, -7);
        const double rotation = 44 * Math.PI / 180;
        StyleId? fill = shape == "hatch-rounded"
            ? document.CreateHatchFillStyle("hatch",
                document.CreateHatchPattern("grid", CadHatchPatternLines.Grid(8)), CadColor.Green)
            : null;
        CadEntity entity = shape switch
        {
            "ellipse" => document.AddEllipse(center, 60, 20),
            "ellipse-arc" => document.AddEllipseArc(center, 60, 20, .1, 4.5),
            _ => document.AddRectangle(CadRectD.FromCenter(center, 120, 40),
                cornerRadiusX: shape == "rectangle" ? 0 : 8,
                cornerRadiusY: shape == "rectangle" ? 0 : 12,
                fillStyleId: fill)
        };
        CadPointD actualEdge;
        switch (entity)
        {
            case CadRectangle rectangle:
                rectangle.SetRotation(rotation);
                actualEdge = rectangle.GeometryTransform.TransformPoint(new(center.X, rectangle.FrameBounds.MaxY));
                break;
            case CadEllipse ellipse:
                ellipse.SetRotation(rotation);
                actualEdge = ellipse.GetPointAtAngle(.7);
                break;
            case CadEllipseArc arc:
                arc.SetRotation(rotation);
                actualEdge = arc.GetPointAtAngle(.7);
                break;
            default: throw new InvalidOperationException();
        }

        var bounds = entity.Bounds;
        var falseEdge = new CadPointD(bounds.Center.X, bounds.MaxY);
        var selected = entity;
        if (mirroredBlock)
        {
            var block = document.CreateBlockDefinition("mirrored", default);
            document.MoveEntityToBlock(entity.Id, block);
            selected = document.AddBlockReference(block, new(20, 5), scaleX: -1, scaleY: 1);
            actualEdge = new(-actualEdge.X + 20, actualEdge.Y + 5);
            falseEdge = new(-falseEdge.X + 20, falseEdge.Y + 5);
        }
        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(2, new(Width / 2, Height / 2));
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsLevelOfDetailEnabled = false, EnableGeometryRealizations = geometryRealizations
        });
        host.Render(CadRenderInvalidation.Full);
        Assert.True(HasPixel(host.CaptureBackBufferPixels(), viewport, actualEdge, highlightOnly: false), "The ordinary oriented entity must be visible at its real edge.");

        var offset = path is "translated" or "uncached-preview" ? new CadVectorD(30, -20) : CadVectorD.Zero;
        if (path == "selection")
        {
            var handles = new CadHandleScene();
            handles.Replace([new CadSelectionEntityReference(selected.Id, selected.Bounds, offset,
                new(Highlight, CadColor.Transparent, Size: 0, StrokeWidth: 2))]);
            host.SetHandleScene(handles);
        }
        else
        {
            if (path == "uncached-preview") host.RemoveEntity(entity.Id);
            var transient = new CadTransientScene();
            transient.Replace([new CadTransientEntityReference(selected.Id, offset,
                new(Highlight, StrokeWidth: 2), UseSourceAppearance: path == "uncached-preview")]);
            host.SetTransientScene(transient);
        }
        host.Render(CadRenderInvalidation.Full);
        var pixels = host.CaptureBackBufferPixels();
        SaveEvidence(pixels, $"{shape}-{path}-{(mirroredBlock ? "mirrored" : "direct")}" +
            (geometryRealizations ? "-realizations" : ""));
        Assert.True(HasPixel(pixels, viewport, actualEdge + offset, highlightOnly: true),
            "The highlight must follow the real rotated edge, including parent reflection and preview translation.");
        Assert.False(HasPixel(pixels, viewport, falseEdge + offset, highlightOnly: true),
            "The axis-aligned bounds edge must not be drawn as the selected geometry.");

        if (entity is CadRectangle { HasRoundedCorners: true } rounded && !mirroredBlock)
        {
            var corner = new CadPointD(rounded.FrameBounds.MaxX - rounded.CornerRadiusX + rounded.CornerRadiusX / Math.Sqrt(2),
                rounded.FrameBounds.MaxY - rounded.CornerRadiusY + rounded.CornerRadiusY / Math.Sqrt(2));
            Assert.True(HasPixel(pixels, viewport, rounded.GeometryTransform.TransformPoint(corner) + offset, true),
                "The rotated rounded corner must preserve its local radii.");
        }
    }

    private static bool HasPixel(byte[] pixels, CadViewport viewport, CadPointD world, bool highlightOnly)
    {
        var screen = viewport.WorldToScreen(world);
        for (var y = Math.Max(0, (int)Math.Round(screen.Y) - 3); y <= Math.Min(Height - 1, (int)Math.Round(screen.Y) + 3); y++)
        for (var x = Math.Max(0, (int)Math.Round(screen.X) - 3); x <= Math.Min(Width - 1, (int)Math.Round(screen.X) + 3); x++)
        {
            var i = (y * Width + x) * 4;
            if (highlightOnly ? pixels[i + 2] > 150 && pixels[i + 1] > 100 && pixels[i] < 140
                : pixels[i] > 40 || pixels[i + 1] > 40 || pixels[i + 2] > 40) return true;
        }
        return false;
    }

    private static void SaveEvidence(byte[] pixels, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_SELECTION_EVIDENCE_DIR");
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
