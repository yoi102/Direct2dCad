using System.Diagnostics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class DrawingConsistencyPixelTests
{
    private const int Width = 640, Height = 480;
    private static readonly CadTransientStyle RedStyle = new(CadColor.Red, StrokeWidth: CadLineWeightDisplay.ToDips(1));

    [Theory]
    [InlineData(false, CadStrokeLineJoin.Bevel, CadStrokeCap.Flat)]
    [InlineData(false, CadStrokeLineJoin.Round, CadStrokeCap.Round)]
    [InlineData(true, CadStrokeLineJoin.Miter, CadStrokeCap.Square)]
    [Trait("Category", "WindowsIntegration")]
    public void DashedPolylinePreviewMatchesCommittedContinuousPath(bool closed, CadStrokeLineJoin join, CadStrokeCap cap)
    {
        CadPointD[] points = [new(-100, -60), new(-53, -60), new(-53, 60)];
        var style = CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.Dash, LineJoin = join, StartCap = cap, EndCap = cap };
        var document = Document(); var polyline = document.AddPolyline(points, closed);
        polyline.SetLineWeight(new(3)); polyline.SetStrokeStyle(style);
        using var final = Host(document, Viewport(2));
        using var preview = Host(Document(), Viewport(2), item: new CadTransientPolyline(points, closed,
            RedStyle with { StrokeWidth = CadLineWeightDisplay.ToDips(3), StrokeStyle = style }));
        AssertSame(final, preview);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(27, 1.5, 3, -5)]
    [Trait("Category", "WindowsIntegration")]
    public void SplineHatchUsesTheSameBoundsAndPatternPhaseBeforeCommit(double angle, double scale, double x, double y)
    {
        CadPointD[] points = [new(-50, -50), new(50, -50), new(50, 50), new(-50, 50)];
        var line = new CadHatchLineDefinition(0, default, new(0, 6));
        var document = Document(); document.GetLayer(LayerId.Default).SetColor(CadColor.Transparent);
        var fill = document.CreateHatchFillStyle("fill", document.CreateHatchPattern("pattern", [line]), CadColor.Green,
            hatchScale: scale, hatchAngle: angle, hatchOrigin: new(x, y));
        document.AddSpline(points, true, fillStyleId: fill);
        using var final = Host(document, Viewport(3));
        using var preview = Host(Document(), Viewport(3), item: new CadTransientSpline(points, true,
            new(CadColor.Transparent, HatchFill: new(CadColor.Green, scale, angle, new(x, y), [line]))));
        AssertSame(final, preview);
    }

    [Theory]
    [InlineData(1_000_000, .01, 1000)]
    [InlineData(100_000_000, .5, 100)]
    [Trait("Category", "WindowsIntegration")]
    public void FarShortLinesRetainTheSameVisiblePixelsAsAtTheOrigin(double coordinate, double length, double zoom)
    {
        var near = Document(); near.AddLine(default, new(length, 0)).SetLineWeight(new(2));
        var far = Document(); far.AddLine(new(coordinate, coordinate), new(coordinate + length, coordinate)).SetLineWeight(new(2));
        using var nearHost = Host(near, Viewport(zoom));
        using var farHost = Host(far, Viewport(zoom, coordinate));
        AssertSame(nearHost, farHost);
        Assert.True(RedCount(farHost.CaptureBackBufferPixels()) >= 80);
    }

    public static IEnumerable<object[]> FarCases()
    {
        foreach (var shape in new[] { "line", "circle", "ellipse", "ellipse-arc", "rectangle", "arc", "polyline", "spline", "region", "composite", "text", "shape-text", "image", "block", "nested-block" })
        foreach (var path in new[] { "entity", "selection", "translated", "uncached" })
        foreach (var background in new[] { false, true })
            yield return [shape, path, background];
    }

    [Theory]
    [MemberData(nameof(FarCases))]
    [Trait("Category", "WindowsIntegration")]
    public void FarGeometryKeepsItsPixelsAcrossResourcesSelectionAndReferencePreviews(string shape, string path, bool background)
    {
        using var near = ShapeHost(shape, path, background, 0);
        using var far = ShapeHost(shape, path, background, 100_000_000);
        AssertSame(near, far);
        Assert.True(RedCount(far.CaptureBackBufferPixels()) > 5);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("ellipse")]
    [InlineData("ellipse-arc")]
    [InlineData("polyline")]
    [InlineData("spline")]
    [InlineData("composite")]
    [InlineData("text")]
    [InlineData("image")]
    [Trait("Category", "WindowsIntegration")]
    public void FarPrimitivePreviewUsesLocalGeometry(string shape)
    {
        byte[] RenderAt(double coordinate)
        {
            var document = Document(); var entity = Shape(document, shape, coordinate);
            var item = Primitive(entity);
            using var host = Host(Document(), Viewport(8, coordinate), item: new CadTransientGroup([item], CadMatrixD.CreateTranslation(3, -2)));
            return host.CaptureBackBufferPixels();
        }
        AssertPixels(RenderAt(0), RenderAt(100_000_000));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void FarSnapshotRefreshesForRotationOpacityAppearanceAndGeometryChanges()
    {
        var document = Document(); var image = (CadImage)Shape(document, "image", 100_000_000);
        using var host = Host(document, Viewport(8, 100_000_000));
        image.SetOpacity(.25); image.SetRotation(.4);
        host.ApplyChanges(document, CadDocumentChangeSet.ForEntity(image.Id, CadEntityChangeKind.Opacity | CadEntityChangeKind.Rotation));
        host.Render(CadRenderInvalidation.Full);
        using var fresh = Host(document, Viewport(8, 100_000_000));
        AssertSame(host, fresh);

        var ellipse = document.AddEllipse(new(100_000_000, 100_000_000), 12, 5);
        host.ApplyChanges(document, CadDocumentChangeSet.ForEntity(ellipse.Id, CadEntityChangeKind.Created));
        ellipse.SetRotation(.7); ellipse.SetStrokeStyle(CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.Dash });
        host.ApplyChanges(document, CadDocumentChangeSet.ForEntity(ellipse.Id, CadEntityChangeKind.Rotation | CadEntityChangeKind.Appearance));
        host.Render(CadRenderInvalidation.Full);
        using var rebuilt = Host(document, Viewport(8, 100_000_000));
        AssertSame(host, rebuilt);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void LargeFarSceneCachePreparationFinishesAndRetainsShortLines()
    {
        var near = Document(); var far = Document();
        for (var i = 0; i < 1100; i++)
        {
            var start = new CadPointD((i % 40 - 20) * 1.5, (i / 40 - 13) * 1.5);
            near.AddLine(start, start + new CadVectorD(.5, 0));
            var shifted = start + new CadVectorD(100_000_000, 100_000_000);
            far.AddLine(shifted, shifted + new CadVectorD(.5, 0));
        }
        using var nearHost = Host(near, Viewport(8));
        using var farHost = Host(far, Viewport(8, 100_000_000), background: true);
        AssertSame(nearHost, farHost);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void FarModelInPaperViewportMatchesOriginModel()
    {
        byte[] RenderAt(double coordinate)
        {
            var document = Document(); Shape(document, "ellipse", coordinate); Shape(document, "polyline", coordinate);
            var layoutId = document.CreateLayout("paper", 64, 48, createDefaultViewport: false);
            document.AddLayoutViewport(layoutId, CadRectD.FromXYWH(8, 8, 48, 32), new(coordinate, coordinate), 1);
            var viewport = new CadViewport(); viewport.SetSize(Width, Height); viewport.SetView(8, new(64, 432));
            using var host = Host(document, viewport);
            host.SetRenderOptions(new() { ActiveLayoutId = layoutId, ActiveOwnerBlockId = document.GetLayout(layoutId).PaperSpaceBlockId,
                DrawGrid = false, DrawOrigin = false, DrawGripHandles = false, IsLevelOfDetailEnabled = false,
                IsAntialiasingEnabled = false, EnableGeometryRealizations = true });
            WaitForCaches(host); host.Render(CadRenderInvalidation.Full); return host.CaptureBackBufferPixels();
        }
        AssertPixels(RenderAt(0), RenderAt(100_000_000));
    }

    [Theory]
    [InlineData(CadGridType.Lines)]
    [InlineData(CadGridType.Dots)]
    [InlineData(CadGridType.Cross)]
    [Trait("Category", "WindowsIntegration")]
    public void FarGridOriginAndGripsKeepTheirScreenPositions(CadGridType gridType)
    {
        byte[] RenderAt(double coordinate)
        {
            var document = Document(); document.ViewSettings.Origin.Position = new(coordinate, coordinate);
            document.ViewSettings.Grid.Type = gridType;
            using var host = Host(document, Viewport(8, coordinate));
            var handles = new CadHandleScene(); handles.Replace([new CadGripHandle(default, new(coordinate + .5, coordinate), CadHandleType.Vertex,
                new(CadColor.Red, CadColor.Red, Size: 8, Shape: CadHandleShape.Diamond))]); host.SetHandleScene(handles);
            host.SetRenderOptions(new() { DrawGrid = true, DrawOrigin = true, DrawGripHandles = true, IsAntialiasingEnabled = false });
            host.Render(CadRenderInvalidation.Full); return host.CaptureBackBufferPixels();
        }
        AssertPixels(RenderAt(0), RenderAt(100_000_000));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void FarMoveUndoRedoAndPartialDrawMatchFreshFullFrames()
    {
        var document = Document(); var ellipse = Shape(document, "ellipse", 100_000_000);
        using var host = Host(document, Viewport(8, 100_000_000));
        var editor = new CadEditor(document); editor.RegisterGeometryResourceManager(host, rebuildExistingResources: false);
        var before = host.CaptureBackBufferPixels();
        editor.Execute(new MoveEntitiesCommand([ellipse.Id], new(3, -2)));
        host.Render(CadRenderInvalidation.FromScreenRect(new(100, 40, 440, 400)));
        using var fresh = Host(document, Viewport(8, 100_000_000)); AssertSame(host, fresh);
        var after = host.CaptureBackBufferPixels();
        editor.Undo(); host.Render(CadRenderInvalidation.Full); AssertPixels(before, host.CaptureBackBufferPixels());
        editor.Redo(); host.Render(CadRenderInvalidation.Full); AssertPixels(after, host.CaptureBackBufferPixels());
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void IndexedFarBlockChildrenRemainVisible()
    {
        byte[] RenderAt(double coordinate)
        {
            var document = Document(); var center = new CadPointD(coordinate, coordinate);
            var block = document.CreateBlockDefinition("indexed", center);
            for (var index = 0; index < 1100; index++)
            {
                var start = center + new CadVectorD((index % 40 - 20) * 1.5, (index / 40 - 13) * 1.5);
                var line = document.AddLine(start, start + new CadVectorD(.5, 0)); document.MoveEntityToBlock(line.Id, block);
            }
            document.AddBlockReference(block, center);
            using var host = Host(document, Viewport(8, coordinate));
            host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, IsAntialiasingEnabled = false, IsLevelOfDetailEnabled = false,
                EntityBoundsQuery = (owner, bounds) => document.Entities.Values.Where(e => e.OwnerBlockId == owner && e.Bounds.Intersects(bounds)).Select(e => e.Id).ToArray() });
            host.Render(CadRenderInvalidation.Full); return host.CaptureBackBufferPixels();
        }
        AssertPixels(RenderAt(0), RenderAt(100_000_000));
    }

    private static Direct2DImageRenderHost ShapeHost(string shape, string path, bool background, double coordinate)
    {
        var document = Document(); var entity = Shape(document, shape, coordinate);
        var host = Host(document, Viewport(8, coordinate), background);
        if (path == "entity") return host;
        var offset = path is "translated" or "uncached" ? new CadVectorD(3, -2) : default;
        if (path == "selection")
        {
            var handles = new CadHandleScene(); handles.Replace([new CadSelectionEntityReference(entity.Id, entity.Bounds, offset,
                new(CadColor.Red, CadColor.Transparent, Size: 0, StrokeWidth: 2))]); host.SetHandleScene(handles);
        }
        else
        {
            if (path == "uncached" && entity is not CadBlockReference) host.RemoveEntity(entity.Id);
            var scene = new CadTransientScene(); scene.Replace([new CadTransientEntityReference(entity.Id, offset, RedStyle)]); host.SetTransientScene(scene);
        }
        host.Render(CadRenderInvalidation.Full); return host;
    }

    private static CadEntity Shape(CadDocument document, string shape, double coordinate)
    {
        var center = new CadPointD(coordinate, coordinate);
        var points = new[] { center + new CadVectorD(-16, -8), center + new CadVectorD(0, 8), center + new CadVectorD(16, -8) };
        CadEntity entity = shape switch
        {
            "line" => document.AddLine(center, center + new CadVectorD(.5, 0)),
            "circle" => document.AddCircle(center, 12),
            "ellipse" => document.AddEllipse(center, 16, 6),
            "ellipse-arc" => document.AddEllipseArc(center, 16, 6, .2, 4.5),
            "arc" => document.AddArc(center, 12, .2, 4.5),
            "rectangle" => document.AddRectangle(CadRectD.FromCenter(center, 32, 16), cornerRadiusX: 3, cornerRadiusY: 4),
            "polyline" => document.AddPolyline(points, false),
            "spline" => document.AddSpline(points, false),
            "region" => document.AddRegion([new CadRegionContour([CadPlanarPrimitive.Arc(center, 12, 0, Math.Tau)])]),
            "composite" => document.AddCompositePath(points[0], [new CadCompositeLineSegment(points[1]), new CadCompositeSplineSegment([points[1], center, points[2]])], false),
            "text" => document.AddText("CAD", center, 5),
            "shape-text" => document.AddShapeText("CAD", center, 5),
            "image" => document.AddImage(CadRectD.FromCenter(center, 24, 12), 1, 1, 4, [0, 0, 255, 255]),
            "block" or "nested-block" => Block(document, center, shape == "nested-block"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        switch (entity)
        {
            case CadEllipse ellipse: ellipse.SetRotation(.7); break;
            case CadEllipseArc arc: arc.SetRotation(.7); break;
            case CadRectangle rectangle: rectangle.SetRotation(.7); break;
            case CadImage image: image.SetRotation(.7); break;
        }
        entity.SetLineWeight(new(1)); return entity;
    }

    private static CadBlockReference Block(CadDocument document, CadPointD center, bool nested)
    {
        var definition = document.CreateBlockDefinition("inner", center);
        var line = document.AddLine(center + new CadVectorD(-12, -5), center + new CadVectorD(12, 5));
        document.MoveEntityToBlock(line.Id, definition);
        if (nested)
        {
            var outer = document.CreateBlockDefinition("outer", default);
            document.AddBlockReference(definition, default, ownerBlockId: outer);
            return document.AddBlockReference(outer, center);
        }
        return document.AddBlockReference(definition, center);
    }

    private static CadTransientItem Primitive(CadEntity entity) => entity switch
    {
        CadLine v => new CadTransientLine(v.Start, v.End, RedStyle),
        CadEllipse v => new CadTransientEllipse(v.Center, v.RadiusX, v.RadiusY, RedStyle, v.RotationRadians),
        CadEllipseArc v => new CadTransientEllipseArc(v.Center, v.RadiusX, v.RadiusY, v.StartAngleRadians, v.SweepAngleRadians, RedStyle, v.RotationRadians),
        CadPolyline v => new CadTransientPolyline(v.Points, v.Closed, RedStyle),
        CadSpline v => new CadTransientSpline(v.FitPoints, v.Closed, RedStyle),
        CadCompositePath v => new CadTransientCompositePath(v.StartPoint, v.Segments, v.Closed, v.Bounds, RedStyle),
        CadText v => new CadTransientText(v.Text, v.Position, v.Height, v.TextBounds, RedStyle),
        CadImage v => new CadTransientImage(v.FrameBounds, v.PixelWidth, v.PixelHeight, v.Stride, v.PixelMemory, RedStyle, RotationRadians: v.RotationRadians),
        _ => throw new NotSupportedException()
    };

    private static CadDocument Document()
    { var document = CadDocument.Create("drawing consistency"); document.GetLayer(LayerId.Default).SetColor(CadColor.Red); return document; }
    private static CadViewport Viewport(double zoom, double coordinate = 0)
    { var viewport = new CadViewport(); viewport.SetSize(Width, Height); viewport.SetView(zoom, new(Width / 2 - coordinate * zoom, Height / 2 + coordinate * zoom)); return viewport; }
    private static Direct2DImageRenderHost Host(CadDocument document, CadViewport viewport, bool background = false, CadTransientItem? item = null)
    {
        var host = new Direct2DImageRenderHost(); host.AttachImageSource(new ImageSource()); host.SetSize(Width, Height);
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsLevelOfDetailEnabled = false, EnableGeometryRealizations = true, IsAntialiasingEnabled = false });
        host.SetScene(document, viewport, prepareResourcesInBackground: background);
        if (item is not null) { var scene = new CadTransientScene(); scene.Replace([item]); host.SetTransientScene(scene); }
        WaitForCaches(host);
        host.Render(CadRenderInvalidation.Full); return host;
    }
    private static void WaitForCaches(Direct2DImageRenderHost host)
    {
        var started = Stopwatch.GetTimestamp();
        while (host.PrepareRenderCacheStep())
        { Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15), "Cache preparation must finish."); Thread.Yield(); }
    }
    private static void AssertSame(Direct2DImageRenderHost expected, Direct2DImageRenderHost actual) => AssertPixels(expected.CaptureBackBufferPixels(), actual.CaptureBackBufferPixels());
    private static void AssertPixels(byte[] expected, byte[] actual)
    {
        var differences = 0;
        for (var i = 0; i < expected.Length; i += 4) if (!expected.AsSpan(i, 4).SequenceEqual(actual.AsSpan(i, 4))) differences++;
        Assert.True(differences == 0, $"Expected identical rasterized geometry, {differences} pixels differ.");
    }
    private static int RedCount(byte[] pixels)
    { var count = 0; for (var i = 0; i < pixels.Length; i += 4) if (pixels[i + 2] > 150 && pixels[i + 1] < 40 && pixels[i] < 40) count++; return count; }
    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = Width;
        public int SurfaceHeight { get; private set; } = Height;
        public void SetSize(int width, int height) => (SurfaceWidth, SurfaceHeight) = (width, height);
        public void SetSurface(nint pointer) { }
        public void Present(Action action, IReadOnlyList<IntRect>? rects = null) => action();
        public void Invalidate() { }
        public void Invalidate(IntRect rect) { }
        public void Invalidate(IReadOnlyList<IntRect> rects) { }
    }
}
