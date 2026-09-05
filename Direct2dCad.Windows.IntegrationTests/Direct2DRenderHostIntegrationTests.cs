using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Overlays;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Editor;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.wpf.Services.Printing.Vector;
using SharpGen.Runtime;
using Vortice.Mathematics;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DRenderHostIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "WindowsIntegration")]
    public void CompositePastePreviewRendersAndMovingItLeavesNoTrails(bool filled)
    {
        const int width = 320, height = 240;
        var geometryDocument = CadDocument.Create("Source");
        var path = geometryDocument.AddCompositePath(new(-20, -20),
            [new CadCompositeLineSegment(new(20, -20)), new CadCompositeArcSegment(new(20, 0), Math.PI / 2),
             new CadCompositeSplineSegment([new(20, 20), new(-20, 20)])], closed: true);
        var document = CadDocument.Create("Target");
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        viewport.SetView(2, new(width / 2, height / 2));
        var style = new CadTransientStyle(CadColor.Red, 1, FillColor: filled ? CadColor.Blue : null);
        var item = new CadTransientCompositePath(path.StartPoint, path.Segments, path.Closed, path.Bounds, style);
        var scene = new CadTransientScene();
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(width, height));
        host.SetSize(width, height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetTransientScene(scene);
        host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        host.Render(CadRenderInvalidation.Full);
        var empty = host.CaptureBackBufferPixels();
        scene.Replace([item]);
        host.Render(CadRenderInvalidation.Full);
        Assert.NotEqual(empty, host.CaptureBackBufferPixels());
        var calculator = new CadRenderInvalidationCalculator(document, viewport, width, height, _ => style);
        var previous = calculator.CreateTransientSceneInvalidation(scene);
        foreach (var offset in new[] { -40, 40, 0 })
        {
            scene.Replace([new CadTransientGroup([item], CadMatrixD.CreateTranslation(offset, 0), style, path.Bounds)]);
            var current = calculator.CreateTransientSceneInvalidation(scene);
            Assert.False(current.IsEmpty);
            host.Render(previous.UnionPreservingCoverage(current), baseSceneChanged: false);
            var partial = host.CaptureBackBufferPixels();
            host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
            Assert.Equal(host.CaptureBackBufferPixels(), partial);
            previous = current;
        }
        scene.Clear();
        host.Render(previous, baseSceneChanged: false);
        Assert.Equal(empty, host.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.7, 2.0)]
    [Trait("Category", "WindowsIntegration")]
    public void LayoutModelEdits_PartialFramesMatchFullRedraw(double rotation, double scale)
    {
        const int width = 640;
        const int height = 480;
        var document = CadDocument.Create("Layout partial rendering");
        var layout = document.GetLayout(LayoutId.Default);
        foreach (var view in layout.Viewports.ToArray())
            document.RemoveLayoutViewport(layout.Id, view.Id);
        document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(15, 20, 110, 130), CadPointD.Origin, scale, rotation);
        document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(165, 20, 110, 130), CadPointD.Origin, scale, -rotation);
        var line = document.AddLine(new(-15, -10), new(15, -10));
        line.SetLineWeight(new CadLineWeight(6));
        var circle = document.AddCircle(new(0, 25), 12);
        var paper = document.AddLine(new(20, 180), new(270, 180));
        document.MoveEntityToBlock(paper.Id, layout.PaperSpaceBlockId);
        var editor = new CadEditor(document);
        var viewport = editor.Viewport;
        viewport.SetSize(width, height);
        viewport.SetView(2, new(20, height - 20));
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(width, height));
        host.SetSize(width, height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        editor.RegisterGeometryResourceManager(host, rebuildExistingResources: false);
        host.SetRenderOptions(new CadRenderOptions
        {
            ActiveLayoutId = layout.Id,
            ActiveOwnerBlockId = layout.PaperSpaceBlockId,
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsLevelOfDetailEnabled = false,
            EntityBoundsQueryInto = editor.SpatialIndex.Query
        });
        var style = new CadPreviewStyleService(document, new CadUserSettings(), keepEntityStrokeWidthScreenConstant: false);
        CadRenderInvalidationCalculator Calculator() =>
            new(document, viewport, width, height, style.CreateEntityPreviewStyle, layout.Id);
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, Calculator());
        host.Render(CadRenderInvalidation.Full);
        Assert.NotEmpty(host.CapturePresentedPixels());

        void Verify(CadDocumentChangeSet change)
        {
            var previous = host.CaptureBackBufferPixels();
            var dirty = tracker.CreateInvalidation(document, change, Calculator());
            Assert.False(dirty.IsFull);
            Assert.False(dirty.IsEmpty);
            host.Render(dirty, baseSceneChanged: true);
            var partial = host.CaptureBackBufferPixels();
            Assert.NotEqual(previous, partial);
            var presented = host.CapturePresentedPixels();
            host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
            Assert.Equal(host.CaptureBackBufferPixels(), partial);
            Assert.Equal(host.CapturePresentedPixels(), presented);
        }

        Verify(editor.SetLineGeometry(line.Id, new(-10, 10), new(20, 10)));
        Verify(editor.SetEntityLineWeight(line.Id, new CadLineWeight(0.25)));
        Verify(editor.DeleteEntity(circle.Id));
        Verify(editor.Undo());
        Verify(editor.SetLineGeometry(paper.Id, new(30, 170), new(250, 170)));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void NestedBlockStrokeEditAndUndo_UpdatePixelsWithoutGeometryChanges()
    {
        var document = CadDocument.Create("Block appearance");
        var inner = document.CreateBlockDefinition("Inner", CadPointD.Origin);
        var outer = document.CreateBlockDefinition("Outer", CadPointD.Origin);
        var child = document.AddLine(new(-20, 0), new(20, 0));
        child.SetLineWeight(new CadLineWeight(2));
        document.MoveEntityToBlock(child.Id, inner);
        var nested = document.AddBlockReference(inner, CadPointD.Origin);
        document.MoveEntityToBlock(nested.Id, outer);
        var reference = document.AddBlockReference(outer, CadPointD.Origin, scaleX: 2, scaleY: 2);
        var editor = new CadEditor(document);
        var viewport = editor.Viewport;
        viewport.SetSize(640, 480);
        viewport.SetView(2, new(320, 240));
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(640, 480));
        host.SetSize(640, 480);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false });
        editor.RegisterGeometryResourceManager(host, rebuildExistingResources: false);
        var style = new CadPreviewStyleService(document, new CadUserSettings());
        CadRenderInvalidationCalculator Calculator() => new(document, viewport, 640, 480,
            style.CreateEntityPreviewStyle, ownerBlockId: BlockId.ModelSpace);
        var tracker = new CadDocumentInvalidationTracker();
        tracker.Reset(document, Calculator());
        host.Render(CadRenderInvalidation.Full);
        var original = host.CaptureBackBufferPixels();

        void RenderChange()
        {
            var changes = editor.LastDocumentChanges;
            Assert.Equal(CadEntityChangeKind.Appearance,
                Assert.Single(changes.EntityChanges, change => change.EntityId == reference.Id).Kind);
            var dirty = tracker.CreateInvalidation(document, changes, Calculator());
            Assert.False(dirty.IsFull);
            host.Render(dirty);
            var partial = host.CaptureBackBufferPixels();
            host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
            Assert.Equal(host.CaptureBackBufferPixels(), partial);
        }

        editor.SetEntityLineWeight(child.Id, new CadLineWeight(0.25));
        RenderChange();
        Assert.NotEqual(original, host.CaptureBackBufferPixels());
        editor.Undo();
        RenderChange();
        Assert.Equal(original, host.CaptureBackBufferPixels());
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void DirtyRegionPlanningUsesCountQueryOncePerBoundsAndResetsEachFrame()
    {
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(320, 240));
        host.SetSize(320, 240);
        var document = CadDocument.Create("Count queries");
        document.AddLine(CadPointD.Origin, new CadPointD(10, 10));
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(1, new CadPointD(160, 120));
        host.SetScene(document, viewport);
        var queries = new Dictionary<CadRectD, int>();
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            EntityBoundsCount = (_, bounds) =>
            {
                queries[bounds] = queries.GetValueOrDefault(bounds) + 1;
                return 1;
            }
        });
        host.Render(CadRenderInvalidation.Full);
        var dirty = CadRenderInvalidation.FromScreenRectsPreservingCoverage(
            [new(10, 10, 20, 20), new(35, 10, 20, 20), new(60, 10, 20, 20)]);
        for (var frame = 0; frame < 2; frame++)
        {
            queries.Clear();
            host.Render(dirty);
            Assert.NotEmpty(queries);
            Assert.All(queries.Values, count => Assert.Equal(1, count));
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task PrintWorker_RunsOffCallerOnStaThread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;

        var result = await CadPrintService.RunOnStaThreadAsync(() =>
            (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));

        Assert.NotEqual(callerThreadId, result.CurrentManagedThreadId);
        Assert.Equal(ApartmentState.STA, result.Item2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void PrintPreview_KeepsCadGeometryVector()
    {
        var document = CadDocument.Create("Vector preview");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var layout = document.GetLayout(LayoutId.Default);
        var request = new CadPrintRequest(
            "Vector preview",
            document,
            layout.PaperBounds,
            layout.Id);

        var preview = Assert.IsType<DrawingImage>(
            CadPrintService.CreatePreviewImage(request));
        var drawings = EnumerateDrawings(preview.Drawing).ToArray();

        Assert.DoesNotContain(drawings, item => item is ImageDrawing);
        Assert.True(drawings.Count(item => item is GeometryDrawing) >= 2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void VectorPrintRenderer_KeepsCadGeometryOutOfPageBitmap()
    {
        var document = CadDocument.Create("Vector printing");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var layout = document.GetLayout(LayoutId.Default);
        var request = new CadPrintRequest(
            "Vector printing",
            document,
            layout.PaperBounds,
            layout.Id);

        var visual = CadVectorPrintRenderer.CreateVisual(
            request,
            layout,
            new System.Windows.Rect(0, 0, 816, 1056),
            embeddedRasterDpi: 1200);
        var drawing = VisualTreeHelper.GetDrawing(visual);
        var drawings = EnumerateDrawings(drawing).ToArray();

        Assert.DoesNotContain(drawings, item => item is ImageDrawing);
        Assert.True(drawings.Count(item => item is GeometryDrawing) >= 2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void VectorPrintPen_ConvertsMillimetersToOutputDips()
    {
        var document = CadDocument.Create("Physical line weight");
        var style = new CadVectorPrintEntityStyle(
            document.GetLayer(LayerId.Default),
            CadColor.Green,
            LineWeight: 25.4,
            CadStrokeStyle.Default,
            LineType: null,
            FillStyle: null);

        var pen = CadVectorPrintStyleResolver.CreatePen(
            style,
            paperScale: 4.0,
            CadMatrixD.Identity);

        Assert.Equal(24.0, pen.Thickness, 8);
        Assert.Equal(96.0, pen.Thickness * 4.0, 8);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void VectorPrintRenderer_KeepsCurvesTextHatchBlocksAndViewportsVector()
    {
        var document = CadDocument.Create("Vector entity printing");
        var hatchPatternId = document.CreateHatchPattern(
            "Print grid",
            CadHatchPatternLines.Grid(2));
        var hatchStyleId = document.CreateHatchFillStyle(
            "Print hatch",
            hatchPatternId,
            CadColor.Green);
        document.AddArc(CadPointD.Origin, 8, 0.2, 2.4);
        document.AddEllipseArc(new CadPointD(20, 0), 10, 4, 0.4, -2.2);
        document.AddSpline(
        [
            new CadPointD(-12, -8),
            new CadPointD(-4, 5),
            new CadPointD(6, -4),
            new CadPointD(12, 6)
        ]);
        document.AddRectangle(
            CadRectD.FromXYWH(-8, -18, 16, 8),
            fillStyleId: hatchStyleId);
        document.AddText("Vector text", new CadPointD(-15, 14), 4, rotationRadians: 0.25);
        document.AddShapeText("CAD", new CadPointD(8, 14), 4, rotationRadians: -0.2);

        var definitionId = document.CreateBlockDefinition("Print block", CadPointD.Origin);
        var definitionLine = document.AddLine(
            new CadPointD(-3, -3),
            new CadPointD(3, 3));
        document.MoveEntityToBlock(definitionLine.Id, definitionId);
        document.AddBlockReference(
            definitionId,
            new CadPointD(25, 15),
            rotationRadians: 0.3,
            scaleX: 1.5,
            scaleY: 0.75);

        var layout = document.GetLayout(LayoutId.Default);
        var request = new CadPrintRequest(
            "Vector entity printing",
            document,
            layout.PaperBounds,
            layout.Id);

        var visual = CadVectorPrintRenderer.CreateVisual(
            request,
            layout,
            new System.Windows.Rect(0, 0, 1120, 792),
            embeddedRasterDpi: 1200);
        var drawings = EnumerateDrawings(VisualTreeHelper.GetDrawing(visual)).ToArray();

        Assert.DoesNotContain(drawings, item => item is ImageDrawing);
        Assert.Contains(drawings, item => item is GlyphRunDrawing);
        Assert.True(drawings.Count(item => item is GeometryDrawing) >= 11);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void VectorPrintRenderer_RasterizesOnlyImageAndOleContent()
    {
        var document = CadDocument.Create("Embedded raster printing");
        var image = document.AddImage(
            CadRectD.FromXYWH(20, 20, 40, 30),
            pixelWidth: 2,
            pixelHeight: 2,
            stride: 8,
            pixels:
            [
                0, 0, 255, 255,
                0, 255, 0, 255,
                255, 0, 0, 255,
                255, 255, 255, 255
            ],
            opacity: 0.75,
            rotationRadians: 0.2);
        var layout = document.GetLayout(LayoutId.Default);
        document.MoveEntityToBlock(image.Id, layout.PaperSpaceBlockId);
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(80, 20, 40, 30),
            [1, 2, 3, 4]);
        document.MoveEntityToBlock(ole.Id, layout.PaperSpaceBlockId);
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var oleRequests = new List<Direct2DOleDrawRequest>();
        var request = new CadPrintRequest(
            "Embedded raster printing",
            document,
            layout.PaperBounds,
            layout.Id,
            oleRequest =>
            {
                oleRequests.Add(oleRequest);
                return new Direct2DOleDrawData(
                    oleRequest.PixelWidth,
                    oleRequest.PixelHeight,
                    checked(oleRequest.PixelWidth * 4),
                    CreatePixels(oleRequest.PixelWidth, oleRequest.PixelHeight));
            });

        var visual = CadVectorPrintRenderer.CreateVisual(
            request,
            layout,
            new System.Windows.Rect(0, 0, 1120, 792),
            embeddedRasterDpi: 1200);
        var drawings = EnumerateDrawings(VisualTreeHelper.GetDrawing(visual)).ToArray();
        var imageDrawings = drawings.OfType<ImageDrawing>().ToArray();
        var imageDrawing = imageDrawings[0];

        var bitmap = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(
            imageDrawing.ImageSource);
        Assert.Equal(2, bitmap.PixelWidth);
        Assert.Equal(2, bitmap.PixelHeight);
        Assert.Equal(1 + oleRequests.Count, imageDrawings.Length);
        Assert.True(oleRequests.Count > 1);
        Assert.All(oleRequests, oleRequest =>
        {
            Assert.InRange(oleRequest.PixelWidth, 1, 1024);
            Assert.InRange(oleRequest.PixelHeight, 1, 1024);
        });
        Assert.True(drawings.Count(item => item is GeometryDrawing) >= 2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void OffscreenRenderer_RendersCadSceneWithoutWpfImageSource()
    {
        var document = CadDocument.Create("Offscreen rendering");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var viewport = new CadViewport();
        viewport.SetSize(128, 128);
        viewport.SetView(4, new CadPointD(64, 64));

        var frame = Direct2DOffscreenRenderer.Render(
            document,
            viewport,
            new CadRenderOptions
            {
                DrawGrid = false,
                DrawOrigin = false,
                DrawGripHandles = false
            },
            128,
            128);

        Assert.Equal(128, frame.PixelWidth);
        Assert.Equal(128, frame.PixelHeight);
        Assert.Equal(128 * 4, frame.Stride);
        Assert.Equal(128 * 128 * 4, frame.Pixels.Length);
        Assert.True(ContainsNonBlackPixel(frame.Pixels));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void OffscreenRenderer_RendersCompleteLayoutPaperAndModelViewport()
    {
        var document = CadDocument.Create("Layout printing");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var layout = document.GetLayout(LayoutId.Default);
        var viewport = new CadViewport();
        viewport.SetSize(840, 594);
        viewport.SetView(2, new CadPointD(0, 594));

        var frame = Direct2DOffscreenRenderer.Render(
            document,
            viewport,
            new CadRenderOptions
            {
                ActiveLayoutId = layout.Id,
                ActiveLayoutViewportId = null,
                DrawGrid = false,
                DrawOrigin = false,
                DrawGripHandles = false,
                DrawLayoutGuides = false
            },
            840,
            594);

        Assert.True(ContainsGreenPixel(
            frame,
            left: 390,
            top: 288,
            right: 450,
            bottom: 306));
    }

    [Theory]
    [InlineData(true, 6.0f, 5.6692915f)]
    [InlineData(false, 6.0f, 6.0f)]
    [Trait("Category", "WindowsIntegration")]
    public void SelectionStrokeWidth_PreservesThickerEntityLineWeight(
        bool keepEntityStrokeWidthScreenConstant,
        float entityModelStrokeWidth,
        float expectedWorldStrokeWidth)
    {
        var viewport = new CadViewport();
        viewport.SetView(4.0, CadPointD.Origin);
        var options = new CadRenderOptions
        {
            KeepStrokeWidthScreenConstant = keepEntityStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = 0.5
        };

        var actual = Direct2DSelectionRenderer.ResolveSelectionStrokeWidth(
            CadHandleStyle.SelectionOutline,
            entityModelStrokeWidth,
            viewport,
            options);

        Assert.Equal(expectedWorldStrokeWidth, actual, precision: 5);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(8.0)]
    [Trait("Category", "WindowsIntegration")]
    public void EntityStrokeWidth_ModelSpaceKeepsPhysicalDisplayWidth(double zoom)
    {
        var viewport = new CadViewport();
        viewport.SetView(zoom, CadPointD.Origin);

        var worldWidth = Direct2DEntityRenderer.ResolveStrokeWidth(
            0.25f,
            viewport,
            new CadRenderOptions
            {
                KeepStrokeWidthScreenConstant = true,
                MinimumScreenStrokeWidth = 0
            });

        Assert.Equal(
            CadLineWeightDisplay.ToDips(0.25),
            worldWidth * zoom,
            precision: 5);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void EntityStrokeWidth_LayoutViewportUsesOwnerWorldScale()
    {
        var viewport = new CadViewport();
        viewport.SetView(12.0, CadPointD.Origin);

        var worldWidth = Direct2DEntityRenderer.ResolveStrokeWidth(
            0.5f,
            viewport,
            new CadRenderOptions
            {
                KeepStrokeWidthScreenConstant = false,
                EntityLineWeightWorldScale = 0.25,
                MinimumScreenStrokeWidth = 0
            });

        Assert.Equal(0.125f, worldWidth, precision: 6);
    }

    private static bool ContainsNonBlackPixel(byte[] pixels)
    {
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
                return true;
        }

        return false;
    }

    private static IEnumerable<Drawing> EnumerateDrawings(Drawing? drawing)
    {
        if (drawing is null)
            yield break;

        yield return drawing;
        if (drawing is not DrawingGroup group)
            yield break;
        foreach (var child in group.Children)
        {
            foreach (var descendant in EnumerateDrawings(child))
                yield return descendant;
        }
    }

    private static bool ContainsGreenPixel(
        Direct2DRenderedFrame frame,
        int left,
        int top,
        int right,
        int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * frame.Stride + x * 4;
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];
                if (green > 160 && green >= red + 50 && green >= blue + 50)
                    return true;
            }
        }

        return false;
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void SelectionStrokeWidth_UsesHighlightWidthForThinEntity()
    {
        var viewport = new CadViewport();
        viewport.SetView(4.0, CadPointD.Origin);

        var actual = Direct2DSelectionRenderer.ResolveSelectionStrokeWidth(
            CadHandleStyle.SelectionOutline,
            entityModelStrokeWidth: 0.2f,
            viewport,
            new CadRenderOptions
            {
                KeepStrokeWidthScreenConstant = true,
                MinimumScreenStrokeWidth = 0.5
            });

        Assert.Equal(0.5f, actual, precision: 5);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RendersEntityChunksOnIndependentDevices()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Multi-device rendering");
        for (var index = 0; index < 96; index++)
        {
            var row = index / 16;
            var column = index % 16;
            document.AddLine(
                new CadPointD(column * 4 - 30, row * 4 - 10),
                new CadPointD(column * 4 - 28, row * 4 - 8));
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(6, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsParallelRenderingEnabled = true,
            ParallelRenderingMode = CadParallelRenderingMode.MultipleDevices,
            ParallelRenderingWorkerCount = 2,
            ParallelRenderingEntityThreshold = 32
        });

        PrepareAllRenderCaches(host);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.ParallelFrameCount);
        Assert.Equal(CadParallelRenderingMode.MultipleDevices, host.RenderStatistics.ParallelMode);
        Assert.Equal(2, host.RenderStatistics.ParallelWorkerCount);
        Assert.Equal(96, host.RenderStatistics.ParallelEntityCount);
        Assert.Equal(96, host.RenderStatistics.VisibleEntityCount);
        Assert.True(host.RenderStatistics.ParallelRenderMilliseconds > 0);
        Assert.Equal(1, imageSource.PresentCount);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RendersEntityChunksOnSharedDeviceContexts()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Shared-device-context rendering");
        for (var index = 0; index < 96; index++)
        {
            var row = index / 16;
            var column = index % 16;
            document.AddLine(
                new CadPointD(column * 4 - 30, row * 4 - 10),
                new CadPointD(column * 4 - 28, row * 4 - 8));
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(6, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsParallelRenderingEnabled = true,
            ParallelRenderingMode = CadParallelRenderingMode.SharedDeviceContexts,
            ParallelRenderingWorkerCount = 2,
            ParallelRenderingEntityThreshold = 32
        });

        PrepareAllRenderCaches(host);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.ParallelFrameCount);
        Assert.Equal(
            CadParallelRenderingMode.SharedDeviceContexts,
            host.RenderStatistics.ParallelMode);
        Assert.Equal(2, host.RenderStatistics.ParallelWorkerCount);
        Assert.Equal(96, host.RenderStatistics.ParallelEntityCount);
        Assert.Equal(96, host.RenderStatistics.VisibleEntityCount);
        Assert.True(host.RenderStatistics.ParallelRenderMilliseconds > 0);
        Assert.Equal(1, imageSource.PresentCount);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_DefersFirstPresentationUntilInitialResourcesAreReady()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Deferred initial presentation");
        for (var index = 0; index < 96; index++)
        {
            document.AddPolyline(
            [
                new CadPointD(index - 48, -10),
                new CadPointD(index - 48, 10)
            ]);
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(6, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        var cacheBuildRequests = 0;
        host.RenderCacheBuildRequested += (_, _) => cacheBuildRequests++;

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(0, imageSource.PresentCount);
        Assert.Equal(1, cacheBuildRequests);

        PrepareAllRenderCaches(host);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, imageSource.PresentCount);
        Assert.Equal(96, host.RenderStatistics.VisibleEntityCount);
    }

    [Fact]
    public void ReattachedSurfacePreparedFirstFrameMatchesCompleteSynchronousScene()
    {
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(640, 480));
        host.SetSize(640, 480);
        var document = CadDocument.Create("Reattached scene");
        for (var index = 0; index < 160; index++)
            document.AddPolyline([new(index - 80, -30), new(index - 80, 30)]);
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(3, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.RebuildAll(document);
        PrepareAllRenderCaches(host);
        host.Render();
        var expected = host.CapturePresentedPixels();

        var source = new RecordingImageSource(640, 480);
        host.AttachImageSource(source);
        host.SetSize(640, 480);
        PrepareAllRenderCaches(host);
        host.Render();
        Assert.Equal(1, source.PresentCount);
        Assert.Equal(160, host.RenderStatistics.VisibleEntityCount);
        Assert.Equal(expected, host.CapturePresentedPixels());
    }

    [Theory]
    [InlineData(CadParallelRenderingMode.MultipleDevices)]
    [InlineData(CadParallelRenderingMode.SharedDeviceContexts)]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_ParallelWorkersReuseGeometryRealizations(
        CadParallelRenderingMode mode)
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Multi-device geometry realization");
        for (var entityIndex = 0; entityIndex < 4; entityIndex++)
        {
            var yOffset = entityIndex * 18.0 - 27.0;
            document.AddPolyline(
                Enumerable
                    .Range(0, 257)
                    .Select(index =>
                    {
                        var x = -50.0 + index * 100.0 / 256.0;
                        return new CadPointD(
                            x,
                            yOffset + Math.Sin(index * Math.PI / 16.0) * 6.0);
                    })
                    .ToArray());
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(4, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = true,
            IsParallelRenderingEnabled = true,
            ParallelRenderingMode = mode,
            ParallelRenderingWorkerCount = 2,
            ParallelRenderingEntityThreshold = 2
        });

        PrepareAllRenderCaches(host);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.ParallelFrameCount);
        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
        Assert.True(host.RenderStatistics.ParallelGpuCacheBytes > 0);
        var firstFrameBuildCount =
            host.RenderStatistics.GeometryRealizationBuildCount;

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.ParallelFrameCount);
        Assert.True(
            host.RenderStatistics.GeometryRealizationBuildCount <=
            firstFrameBuildCount);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheHitCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationStrokeDrawCount >= 1);
    }

    private static void PrepareAllRenderCaches(Direct2DImageRenderHost host)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        while (host.PrepareRenderCacheStep())
        {
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException("Timed out while preparing Direct2D render caches.");

            Thread.Yield();
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_SwitchesParallelResourceStrategiesAtRuntime()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(320, 240);
        host.AttachImageSource(imageSource);
        host.SetSize(320, 240);

        var document = CadDocument.Create("Parallel strategy switch");
        for (var index = 0; index < 64; index++)
        {
            document.AddLine(
                new CadPointD(index - 32, -1),
                new CadPointD(index - 32, 1));
        }

        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(3, new CadPointD(160, 120));
        host.SetScene(document, viewport);

        CadRenderOptions CreateOptions(
            bool enabled,
            CadParallelRenderingMode mode) => new()
            {
                DrawGrid = false,
                DrawOrigin = false,
                DrawGripHandles = false,
                IsParallelRenderingEnabled = enabled,
                ParallelRenderingMode = mode,
                ParallelRenderingWorkerCount = 2,
                ParallelRenderingEntityThreshold = 2
            };

        host.SetRenderOptions(CreateOptions(true, CadParallelRenderingMode.MultipleDevices));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(CadParallelRenderingMode.MultipleDevices, host.RenderStatistics.ParallelMode);

        host.SetRenderOptions(CreateOptions(true, CadParallelRenderingMode.SharedDeviceContexts));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(CadParallelRenderingMode.SharedDeviceContexts, host.RenderStatistics.ParallelMode);

        host.SetRenderOptions(CreateOptions(false, CadParallelRenderingMode.SharedDeviceContexts));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(0, host.RenderStatistics.ParallelFrameCount);
        Assert.Null(host.RenderStatistics.ParallelMode);
        Assert.Equal(64, host.RenderStatistics.VisibleEntityCount);
        Assert.Equal(3, imageSource.PresentCount);
    }

    [Theory]
    [InlineData(CadParallelRenderingMode.MultipleDevices)]
    [InlineData(CadParallelRenderingMode.SharedDeviceContexts)]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_ParallelWorkersRecoverAfterResize(
        CadParallelRenderingMode mode)
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(320, 240);
        host.AttachImageSource(imageSource);
        host.SetSize(320, 240);

        var document = CadDocument.Create("Parallel resize");
        for (var index = 0; index < 32; index++)
            document.AddLine(new CadPointD(index, 0), new CadPointD(index, 4));

        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(3, new CadPointD(160, 120));
        host.SetScene(document, viewport);
        host.SetRenderOptions(CreateParallelRenderOptions(mode));

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        var pool = host.ParallelPoolIdentity;
        Assert.NotNull(pool);
        Assert.Equal(32, host.ParallelPreparedResourceCount);

        host.SetSize(480, 360);
        viewport.SetSize(480, 360);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(480, host.TargetWidth);
        Assert.Equal(360, host.TargetHeight);
        Assert.Equal(mode, host.RenderStatistics.ParallelMode);
        Assert.Equal(32, host.RenderStatistics.ParallelEntityCount);
        Assert.Same(pool, host.ParallelPoolIdentity);
        Assert.Equal(32, host.ParallelPreparedResourceCount);
        Assert.Equal(2, imageSource.PresentCount);
    }

    [Theory]
    [InlineData(CadParallelRenderingMode.MultipleDevices)]
    [InlineData(CadParallelRenderingMode.SharedDeviceContexts)]
    public void CompleteSceneCacheTakesPriorityOverParallelSubmission(CadParallelRenderingMode mode)
    {
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(640, 480));
        host.SetSize(640, 480);
        var document = CadDocument.Create("Retained before parallel");
        for (var index = 0; index < 2048; index++)
            document.AddLine(new(index % 64 * 4, index / 64 * 4),
                new(index % 64 * 4 + 2, index / 64 * 4 + 2));
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(1, new(10, 400));
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(CreateParallelRenderOptions(mode));
        Assert.True(SpinWait.SpinUntil(() => !host.PrepareRenderCacheStep(), TimeSpan.FromSeconds(20)));
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(0, host.RenderStatistics.ParallelFrameCount);
        Assert.True(host.RenderStatistics.TileReplayCount + host.RenderStatistics.CommandListReplayCount > 0);
        Assert.Equal(0, host.ParallelPreparedResourceCount);
        var cached = host.CaptureBackBufferPixels();
        host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(cached, host.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData(CadParallelRenderingMode.MultipleDevices)]
    [InlineData(CadParallelRenderingMode.SharedDeviceContexts)]
    public void ParallelWorkersPrepareOnlyVisibleEntitiesAndKeepPoolAfterEdit(CadParallelRenderingMode mode)
    {
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(320, 240));
        host.SetSize(320, 240);
        var document = CadDocument.Create("Parallel subset");
        for (var index = 0; index < 1000; index++)
            document.AddLine(new(10000 + index * 10, 0), new(10000 + index * 10, 10));
        var visible = Enumerable.Range(0, 32).Select(index =>
            document.AddLine(new(index, 0), new(index, 10))).ToArray();
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(3, new(100, 120));
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(CreateParallelRenderOptions(mode));
        host.Render(CadRenderInvalidation.Full);
        var before = host.CaptureBackBufferPixels();
        var pool = host.ParallelPoolIdentity;
        Assert.NotNull(pool);
        Assert.Equal(32, host.ParallelPreparedResourceCount);
        var move = new Direct2dCad.Commands.MoveEntitiesCommand(visible.Select(line => line.Id), new(0, 20));
        host.ApplyChanges(document, move.Execute(document));
        host.Render(CadRenderInvalidation.Full);
        Assert.Same(pool, host.ParallelPoolIdentity);
        Assert.NotEqual(before, host.CaptureBackBufferPixels());
        Assert.Equal(32, host.ParallelPreparedResourceCount);
        host.ApplyChanges(document, move.Undo(document));
        host.Render(CadRenderInvalidation.Full);
        Assert.Same(pool, host.ParallelPoolIdentity);
        Assert.Equal(before, host.CaptureBackBufferPixels());
        var delete = new Direct2dCad.Commands.DeleteEntitiesCommand([visible[0].Id]);
        host.ApplyChanges(document, delete.Execute(document));
        Assert.Equal(31, host.ParallelPreparedResourceCount);
        host.Render(CadRenderInvalidation.Full);
        Assert.Same(pool, host.ParallelPoolIdentity);
        host.ApplyChanges(document, delete.Undo(document));
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(before, host.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData(CadParallelRenderingMode.MultipleDevices)]
    [InlineData(CadParallelRenderingMode.SharedDeviceContexts)]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_ParallelWorkersRecoverAfterDeviceLoss(
        CadParallelRenderingMode mode)
    {
        var injectedFailureCount = 0;
        using var host = new Direct2DImageRenderHost(context =>
        {
            if (injectedFailureCount++ == 0)
                return Vortice.Direct2D1.ResultCode.RecreateTarget;
            return context.EndDraw();
        });
        var imageSource = new RecordingImageSource(320, 240);
        host.AttachImageSource(imageSource);
        host.SetSize(320, 240);

        var document = CadDocument.Create("Parallel device loss");
        for (var index = 0; index < 32; index++)
            document.AddLine(new CadPointD(index, 0), new CadPointD(index, 4));

        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(3, new CadPointD(160, 120));
        host.SetScene(document, viewport);
        host.SetRenderOptions(CreateParallelRenderOptions(mode));

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(injectedFailureCount >= 2);
        Assert.Equal(1, imageSource.PresentCount);
        Assert.Equal(mode, host.RenderStatistics.ParallelMode);
        Assert.Equal(32, host.RenderStatistics.ParallelEntityCount);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RecordsSceneChunkOnBackgroundDeviceContext()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Background chunk recording");
        for (var index = 0; index < 1152; index++)
        {
            var row = index / 48;
            var column = index % 48;
            document.AddLine(
                new CadPointD(column * 2 - 48, row * 2 - 24),
                new CadPointD(column * 2 - 47, row * 2 - 23));
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(4, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsBackgroundChunkRecordingEnabled = true
        });

        host.Render();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (host.RenderStatistics.BackgroundCommandListBuildCount == 0 &&
               Stopwatch.GetTimestamp() < deadline)
        {
            host.PrepareRenderCacheStep();
            Thread.Yield();
            host.Render();
        }

        Assert.True(host.RenderStatistics.BackgroundCommandListBuildCount > 0);
        Assert.True(
            host.RenderStatistics.BackgroundCommandListBuildMilliseconds > 0);
        Assert.True(host.RenderStatistics.CommandListBuildCount > 0);
        Assert.True(host.RenderStatistics.CommandListCacheBytes > 0);
    }

    [Theory]
    [InlineData(-524287, -524287, 524287, 524287, true)]
    [InlineData(-524288, 0, 10, 10, false)]
    [InlineData(0, 0, 524288, 10, false)]
    [InlineData(double.NaN, 0, 10, 10, false)]
    [Trait("Category", "WindowsIntegration")]
    public void GeometryRealizationBounds_RejectsCoordinatesOutsideDirect2DSafeRange(
        double minX,
        double minY,
        double maxX,
        double maxY,
        bool expected)
    {
        Assert.Equal(
            expected,
            Direct2DGeometryRealizationCache.IsWithinSafeRealizationBounds(
                CadRectD.FromLTRB(minX, minY, maxX, maxY)));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_FallsBackToDirectGeometryForLargeCoordinates()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Large coordinate geometry");
        document.AddPolyline(
            Enumerable
                .Range(0, 64)
                .Select(index => new CadPointD(
                    600_000.0 + index * 2.0,
                    Math.Sin(index * Math.PI / 8.0) * 20.0))
                .ToArray());
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(1.0, new CadPointD(320 - 600_063, 240));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        host.RebuildAll(document);

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 1);
        Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        Assert.Equal(0, host.RenderStatistics.GeometryRealizationCacheMissCount);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_ReusesLevelOfDetailGeometryRealizationAfterColorChange()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("LOD geometry realization");
        var points = Enumerable
            .Range(0, 257)
            .Select(index =>
            {
                var x = -50.0 + index * 100.0 / 256.0;
                return new CadPointD(x, Math.Sin(index * Math.PI / 16.0) * 20.0);
            })
            .ToArray();
        var polyline = document.AddPolyline(points);
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(1.0, new CadPointD(320, 240));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = true
        });
        host.RebuildAll(document);

        PrepareAllRenderCaches(host);

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationBuildMilliseconds >= 0);

        document.Layers[polyline.LayerId].SetColor(CadColor.Green);
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                polyline.Id,
                CadEntityChangeKind.Appearance));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheHitCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationStrokeDrawCount >= 1);

        polyline.SetLineWeight(new CadLineWeight(2.0));
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                polyline.Id,
                CadEntityChangeKind.Appearance));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.49, 1)]
    [InlineData(1.5, 2)]
    [InlineData(-0.49, 0)]
    [InlineData(-0.5, -1)]
    [Trait("Category", "WindowsIntegration")]
    public void PanPreviewTranslation_QuantizesFractionalOffsetForGridContinuity(
        double translation,
        double expected)
    {
        Assert.Equal(
            expected,
            Direct2DImageRenderHost.QuantizePanPreviewTranslation(translation));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void PanPreviewExposedRegions_IncludeGridSeamOverlapOnBothAxes()
    {
        var exposed = Direct2DImageRenderHost.ResolveSnapshotExposedRects(
            targetWidth: 320,
            targetHeight: 240,
            scale: 1.0,
            translationX: 37,
            translationY: -19);

        Assert.NotNull(exposed);
        Assert.Equal(
        [
            new CadScreenRect(0, 219, 320, 21),
            new CadScreenRect(0, 0, 39, 219)
        ],
            exposed);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MovingInfiniteCross_PartialFramesMatchAFullRedrawWithoutTrails()
    {
        const int width = 320;
        const int height = 240;
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(width, height);
        host.AttachImageSource(imageSource);
        host.SetSize(width, height);

        var document = CadDocument.Create("Infinite cross trail regression");
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        viewport.SetView(1.0, new CadPointD(width / 2.0, height / 2.0));
        var scene = new CadTransientScene();
        var style = new CadTransientStyle(
            CadColor.FromRgb(255, 214, 92),
            1.25);
        var positions = new[]
        {
            new CadPointD(-120, 70),
            new CadPointD(110, -75),
            new CadPointD(-90, -60),
            new CadPointD(95, 65),
            new CadPointD(0, 0)
        };

        host.SetScene(document, viewport);
        host.SetTransientScene(scene);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });

        scene.Replace([new CadTransientInfiniteCross(positions[0], style)]);
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        var previousDamage = CreateInfiniteCrossDamage(
            viewport,
            positions[0],
            width,
            height);

        foreach (var position in positions.Skip(1))
        {
            scene.Replace([new CadTransientInfiniteCross(position, style)]);
            var currentDamage = CreateInfiniteCrossDamage(
                viewport,
                position,
                width,
                height);
            host.Render(
                previousDamage.UnionPreservingCoverage(currentDamage),
                baseSceneChanged: false);
            previousDamage = currentDamage;
        }

        var partialBackBufferPixels = host.CaptureBackBufferPixels();
        var partialPresentedPixels = host.CapturePresentedPixels();
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var fullBackBufferPixels = host.CaptureBackBufferPixels();
        var fullPresentedPixels = host.CapturePresentedPixels();

        Assert.Equal(fullBackBufferPixels, partialBackBufferPixels);
        Assert.Equal(fullPresentedPixels, partialPresentedPixels);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MovingSnapMarkerX_PartialFramesMatchAFullRedrawWithoutTrails()
    {
        const int width = 320;
        const int height = 240;
        const double halfSize = 7.0;
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(width, height);
        host.AttachImageSource(imageSource);
        host.SetSize(width, height);

        var document = CadDocument.Create("Snap marker X trail regression");
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        viewport.SetView(1.0, new CadPointD(width / 2.0, height / 2.0));
        var scene = new CadTransientScene();
        var style = new CadTransientStyle(
            CadColor.FromRgb(255, 214, 92),
            1.25);
        var positions = new[]
        {
            new CadPointD(-120, 70),
            new CadPointD(110, -75),
            new CadPointD(-90, -60),
            new CadPointD(95, 65),
            new CadPointD(0, 0)
        };

        host.SetScene(document, viewport);
        host.SetTransientScene(scene);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });

        scene.Replace(CreateSnapMarkerX(positions[0], halfSize, style));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        var previousDamage = CreateSnapMarkerXDamage(
            viewport,
            positions[0],
            halfSize,
            width,
            height);

        foreach (var position in positions.Skip(1))
        {
            scene.Replace(CreateSnapMarkerX(position, halfSize, style));
            var currentDamage = CreateSnapMarkerXDamage(
                viewport,
                position,
                halfSize,
                width,
                height);
            host.Render(
                previousDamage.UnionPreservingCoverage(currentDamage),
                baseSceneChanged: false);
            previousDamage = currentDamage;
        }

        var partialBackBufferPixels = host.CaptureBackBufferPixels();
        var partialPresentedPixels = host.CapturePresentedPixels();
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var fullBackBufferPixels = host.CaptureBackBufferPixels();
        var fullPresentedPixels = host.CapturePresentedPixels();

        Assert.Equal(fullBackBufferPixels, partialBackBufferPixels);
        Assert.Equal(fullPresentedPixels, partialPresentedPixels);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MovingSnapMarkerX_RandomSubpixelFramesMatchAFullRedrawWithoutTrails()
    {
        const int width = 320;
        const int height = 240;
        const double markerLength = 14.0;
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(width, height);
        host.AttachImageSource(imageSource);
        host.SetSize(width, height);

        var document = CadDocument.Create("Random snap marker X trail regression");
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        var scene = new CadTransientScene();
        var style = new CadTransientStyle(
            CadColor.FromRgb(255, 214, 92),
            1.25);
        var random = new Random(1729);

        host.SetScene(document, viewport);
        host.SetTransientScene(scene);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });

        foreach (var zoom in new[] { 0.125, 0.75, 1.0, 4.0, 24.0 })
        {
            viewport.SetView(zoom, new CadPointD(width / 2.0, height / 2.0));
            var halfSize = markerLength * 0.5 / zoom;
            CadPointD? previousPosition = null;
            CadRenderInvalidation previousDamage = CadRenderInvalidation.Empty;

            for (var index = 0; index < 24; index++)
            {
                var screen = new CadPointD(
                    14.25 + random.NextDouble() * (width - 28.5),
                    14.25 + random.NextDouble() * (height - 28.5));
                var position = viewport.ScreenToWorld(screen);
                scene.Replace(CreateSnapMarkerX(position, halfSize, style));
                var currentDamage = CreateSnapMarkerXDamage(
                    viewport,
                    position,
                    halfSize,
                    width,
                    height);

                if (previousPosition is null)
                {
                    host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                }
                else
                {
                    host.Render(
                        previousDamage.UnionPreservingCoverage(currentDamage),
                        baseSceneChanged: false);
                }

                previousPosition = position;
                previousDamage = currentDamage;
            }
        }

        var partialBackBufferPixels = host.CaptureBackBufferPixels();
        var partialPresentedPixels = host.CapturePresentedPixels();
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var fullBackBufferPixels = host.CaptureBackBufferPixels();
        var fullPresentedPixels = host.CapturePresentedPixels();

        Assert.Equal(fullBackBufferPixels, partialBackBufferPixels);
        Assert.Equal(fullPresentedPixels, partialPresentedPixels);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void DeviceFailureClassifier_RecognizesOnlyRecoverableDeviceFailures()
    {
        Result[] recoverableResults =
        [
            Vortice.Direct2D1.ResultCode.RecreateTarget,
            Vortice.DXGI.ResultCode.DeviceRemoved,
            Vortice.DXGI.ResultCode.DeviceReset,
            Vortice.DXGI.ResultCode.DeviceHung,
            Vortice.DXGI.ResultCode.DriverInternalError,
            Vortice.DXGI.ResultCode.AccessLost
        ];

        Assert.All(
            recoverableResults,
            result => Assert.True(Direct2DDeviceFailureClassifier.IsRecoverable(result)));
        Assert.False(Direct2DDeviceFailureClassifier.IsRecoverable(Result.Ok));
        Assert.False(Direct2DDeviceFailureClassifier.IsRecoverable(
            Vortice.Direct2D1.ResultCode.WrongState));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void DeviceResourceRecovery_RebindsSurfaceAndRemainsDrawable()
    {
        using var target = new ImageSourceDirect2DResource();
        var imageSource = new RecordingImageSource(160, 120);
        target.SetTarget(imageSource);
        target.DrawFrame(
            context => context.Clear(new Color4(0.1f, 0.2f, 0.3f, 1.0f)));
        Assert.True(target.CaptureBaseScene(null));
        Assert.True(target.HasBaseSceneSnapshot);
        var setSurfaceCountBeforeRecovery = imageSource.SetSurfaceCount;

        target.RecoverFromDeviceLoss();

        Assert.True(target.IsTargetReady);
        Assert.False(target.HasBaseSceneSnapshot);
        Assert.Equal(setSurfaceCountBeforeRecovery + 2, imageSource.SetSurfaceCount);
        Assert.Equal(nint.Zero, imageSource.SurfaceAssignments[^2]);
        Assert.NotEqual(nint.Zero, imageSource.SurfaceAssignments[^1]);

        target.DrawFrame(
            context => context.Clear(new Color4(0.3f, 0.2f, 0.1f, 1.0f)));

        Assert.Equal(2, imageSource.PresentCount);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_EndDrawDeviceLossRecreatesResourcesAndPresentsRetriedFrameOnce()
    {
        var endDrawCallCount = 0;
        var injectedFailureCount = 0;
        using var host = new Direct2DImageRenderHost(context =>
        {
            endDrawCallCount++;
            if (injectedFailureCount == 0)
            {
                injectedFailureCount++;
                return Vortice.Direct2D1.ResultCode.RecreateTarget;
            }

            return context.EndDraw();
        });
        var imageSource = new RecordingImageSource(240, 160);
        host.AttachImageSource(imageSource);
        host.SetSize(240, 160);
        var document = CadDocument.Create("Device loss retry");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var viewport = new CadViewport();
        viewport.SetSize(240, 160);
        viewport.SetView(6, new CadPointD(120, 80));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        host.RebuildAll(document);

        host.Render();

        Assert.Equal(1, injectedFailureCount);
        Assert.True(endDrawCallCount >= 2);
        Assert.Equal(1, imageSource.PresentCount);
        Assert.Equal(1, imageSource.SurfaceAssignments.Count(pointer => pointer == nint.Zero));
        Assert.NotEqual(nint.Zero, imageSource.SurfaceAssignments[^1]);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 1);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_CreatesNativeResourcesAndPresentsFullAndPartialFrames()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(320, 240);
        host.AttachImageSource(imageSource);
        host.SetSize(320, 240);

        var document = CadDocument.Create("Native render integration");
        var solidFillStyleId = document.CreateSolidFillStyle(
            "Integration solid",
            CadColor.Green);
        var hatchPatternId = document.CreateHatchPattern(
            "Integration grid",
            CadHatchPatternLines.Grid(3));
        var hatchFillStyleId = document.CreateHatchFillStyle(
            "Integration hatch",
            hatchPatternId,
            CadColor.Blue,
            hatchScale: 0.75,
            hatchAngle: Math.PI / 8,
            hatchOrigin: new CadPointD(-12, -8));
        var line = document.AddLine(
            new CadPointD(-20, -10),
            new CadPointD(20, 10));
        document.AddCircle(
            new CadPointD(-15, 0),
            5,
            fillStyleId: solidFillStyleId);
        document.AddEllipse(
            new CadPointD(14, 0),
            6,
            3,
            fillStyleId: hatchFillStyleId);
        document.AddArcDegrees(new CadPointD(-12, -12), 6, 15, 235);
        document.AddEllipseArc(
            new CadPointD(13, -12),
            7,
            3,
            0.2,
            2.4);
        document.AddRectangle(
            CadRectD.FromLTRB(-6, -7, 6, 1),
            cornerRadiusX: 1.5,
            cornerRadiusY: 1.5,
            fillStyleId: hatchFillStyleId);
        document.AddPolyline(
            [
                new CadPointD(-8, 8),
                new CadPointD(-2, 14),
                new CadPointD(3, 8)
            ],
            isClosed: true,
            fillStyleId: solidFillStyleId);
        document.AddSpline(
            [
                new CadPointD(5, 8),
                new CadPointD(10, 15),
                new CadPointD(18, 9),
                new CadPointD(12, 6)
            ],
            closed: true,
            fillStyleId: hatchFillStyleId);
        var text = document.AddText("DirectWrite", new CadPointD(-10, 15), 5);
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(5, new CadPointD(160, 120));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        var transientStyle = new CadTransientStyle(CadColor.Green, 1.5);
        var transientScene = new CadTransientScene();
        transientScene.Replace(
        [
            new CadTransientLine(
                new CadPointD(-18, 18),
                new CadPointD(18, 18),
                transientStyle),
            new CadTransientCircle(new CadPointD(18, -12), 4, transientStyle)
        ]);
        var handleScene = new CadHandleScene();
        handleScene.Replace(
        [
            new CadSelectionEntityReference(
                line.Id,
                line.Bounds,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline),
            new CadGripHandle(
                line.Id,
                line.Start,
                CadHandleType.Vertex,
                CadHandleStyle.Grip)
        ]);
        host.SetTransientScene(transientScene);
        host.SetHandleScene(handleScene);
        host.RebuildAll(document);

        var textChanges = host.UpdateTextMeasurements(document);
        Assert.False(text.RequiresBoundsMeasurement);
        Assert.Contains(textChanges.EntityChanges, change => change.EntityId == text.Id);

        host.Render();

        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
        Assert.Equal(320, host.TargetWidth);
        Assert.Equal(240, host.TargetHeight);
        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.VisibleEntityCount >= 9);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 9);
        Assert.Equal(1, host.RenderStatistics.SelectionEntityCount);
        Assert.True(host.RenderStatistics.TransientRenderMilliseconds >= 0);
        Assert.True(host.RenderStatistics.SelectionRenderMilliseconds >= 0);

        var previousBounds = line.Bounds;
        line.SetGeometry(new CadPointD(-10, -5), new CadPointD(25, 12));
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry));
        var dirtyBounds = previousBounds.Union(line.Bounds);
        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            dirtyBounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(invalidation, baseSceneChanged: true);

        Assert.Equal(2, imageSource.PresentCount);
        var dirtyRects = Assert.IsAssignableFrom<IReadOnlyList<IntRect>>(
            imageSource.LastDirtyRects);
        Assert.NotEmpty(dirtyRects);
        Assert.All(dirtyRects, rect =>
        {
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
            Assert.InRange(rect.X, 0, 319);
            Assert.InRange(rect.Y, 0, 239);
        });
        Assert.False(host.RenderStatistics.IsFullFrame);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RecreatesTargetWhenSurfaceSizeChanges()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(64, 64);
        host.AttachImageSource(imageSource);

        host.SetSize(192, 128);

        Assert.Equal(192, host.TargetWidth);
        Assert.Equal(128, host.TargetHeight);
        Assert.Equal(192, imageSource.SurfaceWidth);
        Assert.Equal(128, imageSource.SurfaceHeight);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
        Assert.True(imageSource.SetSurfaceCount >= 2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_DrawsGridAndOriginWithoutAntialiasing()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(256, 192);
        host.AttachImageSource(imageSource);
        host.SetSize(256, 192);

        var document = CadDocument.Create("Native background integration");
        var viewport = new CadViewport();
        viewport.SetSize(256, 192);
        viewport.SetView(8, new CadPointD(128, 96));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = true,
            DrawOrigin = true,
            DrawGripHandles = false,
            IsAntialiasingEnabled = false,
            IsTextAntialiasingEnabled = false
        });

        host.Render();

        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.Equal(0, host.RenderStatistics.VisibleEntityCount);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RendersShapeTextImageOleAndBlockReferenceWithPartialInvalidation()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(420, 320);
        host.AttachImageSource(imageSource);
        host.SetSize(420, 320);

        var oleDrawRequests = new List<Direct2DOleDrawRequest>();
        host.SetOleDrawCallback(request =>
        {
            oleDrawRequests.Add(request);
            return new Direct2DOleDrawData(
                request.PixelWidth,
                request.PixelHeight,
                checked(request.PixelWidth * 4),
                CreatePixels(request.PixelWidth, request.PixelHeight));
        });

        var document = CadDocument.Create("Binary entity integration");
        document.AddShapeText("CAD", new CadPointD(-45, 20), 8);
        var image = document.AddImage(
            CadRectD.FromXYWH(-20, -15, 10, 8),
            pixelWidth: 2,
            pixelHeight: 2,
            stride: 8,
            CreatePixels(2, 2),
            opacity: 0.7,
            rotationRadians: Math.PI / 12);
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(0, -15, 12, 8),
            [1, 2, 3, 4],
            opacity: 0.65);
        var blockId = document.CreateBlockDefinition("Integration block", CadPointD.Origin);
        var blockChild = document.AddLine(CadPointD.Origin, new CadPointD(8, 6));
        document.MoveEntityToBlock(blockChild.Id, blockId);
        var blockReference = document.AddBlockReference(
            blockId,
            new CadPointD(20, 10),
            scaleX: 2,
            scaleY: 2);
        var viewport = new CadViewport();
        viewport.SetSize(420, 320);
        viewport.SetView(4, new CadPointD(210, 160));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = false
        });
        host.RebuildAll(document);

        host.Render();

        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.VisibleEntityCount >= 4);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 4);
        Assert.True(host.RenderStatistics.BlockReferenceCount >= 1);
        Assert.True(host.RenderStatistics.ExpandedBlockEntityCount >= 1);
        Assert.True(host.RenderStatistics.ImageBitmapCacheBytes > 0);
        Assert.True(host.RenderStatistics.OleTileBuildCount >= 1);
        Assert.NotEmpty(oleDrawRequests);
        Assert.All(oleDrawRequests, request =>
        {
            Assert.Equal(ole.Id, request.RenderKey.EntityId);
            Assert.True(request.PixelWidth > 0);
            Assert.True(request.PixelHeight > 0);
            Assert.True(request.FullPixelWidth >= request.PixelWidth);
            Assert.True(request.FullPixelHeight >= request.PixelHeight);
        });

        var oldBounds = image.Bounds.Union(ole.Bounds).Union(blockReference.Bounds);
        image.SetBounds(CadRectD.FromXYWH(-32, -18, 18, 12));
        ole.SetBounds(CadRectD.FromXYWH(2, -20, 18, 12));
        blockReference.SetPosition(new CadPointD(12, 18));
        document.RefreshBlockReferenceBounds();
        var changes = CadDocumentChangeSet.Combine(
        [
            CadDocumentChangeSet.ForEntity(image.Id, CadEntityChangeKind.Geometry),
            CadDocumentChangeSet.ForEntity(ole.Id, CadEntityChangeKind.Geometry),
            CadDocumentChangeSet.ForEntity(blockReference.Id, CadEntityChangeKind.Geometry)
        ]);
        host.ApplyChanges(document, changes);
        var dirtyBounds = oldBounds
            .Union(image.Bounds)
            .Union(ole.Bounds)
            .Union(blockReference.Bounds);
        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            dirtyBounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(invalidation, baseSceneChanged: true);

        Assert.Equal(2, imageSource.PresentCount);
        Assert.False(host.RenderStatistics.IsFullFrame);
        var dirtyRects = Assert.IsAssignableFrom<IReadOnlyList<IntRect>>(
            imageSource.LastDirtyRects);
        Assert.NotEmpty(dirtyRects);
        Assert.All(dirtyRects, rect =>
        {
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
            Assert.InRange(rect.X, 0, 419);
            Assert.InRange(rect.Y, 0, 319);
        });

        var requestCountBeforeDataChange = oleDrawRequests.Count;
        var updatedOleBytes = new byte[] { 9, 8, 7, 6 };
        ole.SetOleData(updatedOleBytes, "application/updated", "updated.ole");
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                ole.Id,
                CadEntityChangeKind.Appearance | CadEntityChangeKind.EmbeddedData));
        var oleInvalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            ole.Bounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(oleInvalidation, baseSceneChanged: true);

        Assert.Equal(3, imageSource.PresentCount);
        Assert.True(oleDrawRequests.Count > requestCountBeforeDataChange);
        Assert.All(
            oleDrawRequests.Skip(requestCountBeforeDataChange),
            request => Assert.Equal(updatedOleBytes, request.OleBytes.ToArray()));
    }

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x30;
            pixels[index + 1] = 0x90;
            pixels[index + 2] = 0xE0;
            pixels[index + 3] = 0xFF;
        }

        return pixels;
    }

    private static CadRenderInvalidation CreateInfiniteCrossDamage(
        CadViewport viewport,
        CadPointD center,
        int width,
        int height)
    {
        const int padding = 12;
        var screen = viewport.WorldToScreen(center);
        var x = (int)Math.Round(screen.X);
        var y = (int)Math.Round(screen.Y);
        return CadRenderInvalidation.FromScreenRectsPreservingCoverage(
        [
            new CadScreenRect(0, y - padding, width, padding * 2),
            new CadScreenRect(x - padding, 0, padding * 2, height)
        ]);
    }

    private static IReadOnlyList<CadTransientItem> CreateSnapMarkerX(
        CadPointD center,
        double halfSize,
        CadTransientStyle style) =>
    [
        new CadTransientLine(
            new CadPointD(center.X - halfSize, center.Y - halfSize),
            new CadPointD(center.X + halfSize, center.Y + halfSize),
            style),
        new CadTransientLine(
            new CadPointD(center.X - halfSize, center.Y + halfSize),
            new CadPointD(center.X + halfSize, center.Y - halfSize),
            style)
    ];

    private static CadRenderInvalidation CreateSnapMarkerXDamage(
        CadViewport viewport,
        CadPointD center,
        double halfSize,
        int width,
        int height)
    {
        const int padding = 12;
        var screen = viewport.WorldToScreen(center);
        var x = (int)Math.Floor(screen.X - halfSize) - padding;
        var y = (int)Math.Floor(screen.Y - halfSize) - padding;
        var size = (int)Math.Ceiling(halfSize * 2) + padding * 2 + 2;
        return CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(x, y, Math.Min(size, width - x), Math.Min(size, height - y)));
    }

    private static CadRenderOptions CreateParallelRenderOptions(
        CadParallelRenderingMode mode) => new()
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsParallelRenderingEnabled = true,
            ParallelRenderingMode = mode,
            ParallelRenderingWorkerCount = 2,
            ParallelRenderingEntityThreshold = 2
        };

    private sealed class RecordingImageSource(int width, int height) : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = width;
        public int SurfaceHeight { get; private set; } = height;
        public nint SurfacePointer { get; private set; }
        public int SetSurfaceCount { get; private set; }
        public int PresentCount { get; private set; }
        public IReadOnlyList<IntRect>? LastDirtyRects { get; private set; }
        public List<nint> SurfaceAssignments { get; } = [];

        public void SetSize(int width, int height)
        {
            SurfaceWidth = width;
            SurfaceHeight = height;
        }

        public void SetSurface(nint surface9Ptr)
        {
            SurfacePointer = surface9Ptr;
            SetSurfaceCount++;
            SurfaceAssignments.Add(surface9Ptr);
        }

        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
        {
            presentAction();
            PresentCount++;
            LastDirtyRects = dirtyRects?.ToArray();
        }

        public void Invalidate()
        {
        }

        public void Invalidate(IntRect dirtyRect)
        {
        }

        public void Invalidate(IReadOnlyList<IntRect> dirtyRects)
        {
        }
    }
}
