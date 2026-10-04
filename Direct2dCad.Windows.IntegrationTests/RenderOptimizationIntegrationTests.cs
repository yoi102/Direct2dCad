using System.Diagnostics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class RenderOptimizationIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingDrawCallbackDoesNotPublishOrPoisonTheNextFrame(bool multipleRegions)
    {
        using var target = new ImageSourceDirect2DResource();
        var image = new RecordingImageSource(64, 64);
        target.SetTarget(image);
        target.DrawFrame(context => context.Clear(new Color4(0, 1, 0, 1)));
        var original = target.CapturePresentedPixels();

        void Throw(ID2D1DeviceContext context)
        {
            context.Clear(new Color4(1, 0, 0, 1));
            throw new InvalidOperationException("Injected draw failure");
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (multipleRegions)
                target.DrawFrame(Throw, new[] { new CadScreenRect(0, 0, 64, 64) });
            else
                target.DrawFrame(Throw);
        });
        Assert.Equal(1, image.PresentCount);
        Assert.Equal(original, target.CapturePresentedPixels());
        target.DrawFrame(context => context.Clear(new Color4(0, 0, 1, 1)));
        Assert.Equal(2, image.PresentCount);
        Assert.NotEqual(original, target.CapturePresentedPixels());
    }

    [Fact]
    public void ChangingViewReprioritizesUnpreparedGeometryAndPresentsCompleteVisiblePixels()
    {
        var document = CadDocument.Create("Moving preparation view");
        document.AddLine(new(-20, 0), new(20, 0));
        for (var index = 0; index < 4_000; index++)
            document.AddLine(new(20_000 + index, 0), new(20_000 + index, 20));
        document.AddPolyline([new(9_980, -20), new(10_020, 0), new(9_980, 20)]);
        var viewport = new CadViewport();
        viewport.SetSize(64, 64);
        viewport.SetView(1, new(32, 32));
        using var host = CreateHost(document, viewport);
        host.RenderCacheBuildRequested += (_, _) => { };
        WaitUntilVisible(host);
        host.Render(CadRenderInvalidation.Full);
        Assert.True(host.HasPresentedScene);
        Assert.True(host.PrepareRenderCacheStep());
        Assert.True(host.BeginViewportInteraction());

        viewport.SetView(1, new(-9_968, 32));
        Assert.False(host.IsInitialViewReady);
        Assert.False(host.RenderViewportInteractionPreview());
        WaitUntilVisible(host);
        Assert.True(host.PrepareRenderCacheStep()); // The new view does not wait for the whole drawing.
        host.Render(CadRenderInvalidation.Full);

        using var reference = CreateHost(document, viewport);
        reference.RebuildAll(document);
        reference.Render(CadRenderInvalidation.Full);
        Assert.Equal(reference.CaptureBackBufferPixels(), host.CaptureBackBufferPixels());
    }

    [Fact]
    public void PriorityTracksAlreadyCapturedResultsAndDropsThePreviousViewWaitSet()
    {
        var document = CadDocument.Create("In-flight priority");
        var first = document.AddPolyline([new(0, 0), new(20, 0)]);
        var second = document.AddPolyline([new(100, 0), new(120, 0)]);
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation = new Direct2DGeometryPreparationService(factory);
        preparation.Schedule(document);
        preparation.Prioritize([first.Id]);
        preparation.CaptureStep(new ResourcePreparationBudget(8, TimeSpan.FromSeconds(1)));
        preparation.Prioritize([second.Id]);
        using var preparedFirst = WaitForNext(preparation);
        preparation.MarkApplied(preparedFirst.EntityId);
        Assert.True(preparation.HasVisiblePending);
        using var preparedSecond = WaitForNext(preparation);
        preparation.MarkApplied(preparedSecond.EntityId);
        Assert.False(preparation.HasVisiblePending);
    }

    [Fact]
    public void ZoomFramesUseExactGeometryAndStableFramesResumeRealizationReuse()
    {
        var document = CadDocument.Create("Adaptive realization");
        document.AddPolyline(Enumerable.Range(0, 65)
            .Select(index => new CadPointD(-20 + index * 40.0 / 64, Math.Sin(index * .3) * 15)));
        var viewport = new CadViewport();
        viewport.SetSize(64, 64);
        viewport.SetView(1, new(32, 32));
        using var host = CreateHost(document, viewport);
        host.RebuildAll(document);
        host.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount > 0);

        for (var step = 0; step < 4; step++)
        {
            viewport.ZoomAt(new(32, 32), 1.1);
            host.Render(CadRenderInvalidation.Full);
            Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
            Assert.Equal(0, host.RenderStatistics.GeometryRealizationStrokeDrawCount);
        }

        host.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.GeometryRealizationStrokeDrawCount > 0);
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheHitCount > 0);
    }

    [Fact]
    public void FirstVisiblePriorityIncludesWideStrokesOutsideCenterlineBounds()
    {
        var document = CadDocument.Create("Wide visible stroke");
        for (var index = 0; index < 4_000; index++)
            document.AddLine(new(20_000 + index, 0), new(20_000 + index, 20));
        var wide = document.AddLine(new(-20, 110), new(20, 110));
        wide.SetLineWeight(new CadLineWeight(50));
        var viewport = new CadViewport();
        viewport.SetSize(64, 64);
        viewport.SetView(1, new(32, 32));
        using var host = CreateHost(document, viewport);
        host.RenderCacheBuildRequested += (_, _) => { };
        WaitUntilVisible(host);
        Assert.True(host.PrepareRenderCacheStep());
        host.Render(CadRenderInvalidation.Full);
        using var reference = CreateHost(document, viewport);
        reference.RebuildAll(document);
        reference.Render(CadRenderInvalidation.Full);
        Assert.Equal(reference.CaptureBackBufferPixels(), host.CaptureBackBufferPixels());
        Assert.Contains(host.CaptureBackBufferPixels().Where((_, index) => index % 4 != 3), value => value > 0);
    }

    [Fact]
    public void LayoutVisiblePriorityIncludesModelViewportGeometryBeforeOffscreenResources()
    {
        var document = CadDocument.Create("Layout preparation");
        var redLayer = document.CreateLayer("Red", CadColor.FromRgb(255, 0, 0), CadLineWeight.Default);
        for (var index = 0; index < 4_000; index++)
            document.AddLine(new(20_000 + index, 0), new(20_000 + index, 20));
        document.AddPolyline([new(9_980, -20), new(10_020, 0), new(9_980, 20)], layerId: redLayer)
            .SetLineWeight(new CadLineWeight(3));
        document.AddPolyline([new(14_980, -20), new(15_020, 20)], layerId: redLayer)
            .SetLineWeight(new CadLineWeight(3));
        var layoutId = document.CreateLayout("Visible model viewport", 64, 64, createDefaultViewport: false);
        var modelViewId = document.AddLayoutViewport(layoutId,
            CadRectD.FromXYWH(8, 8, 48, 48), new(10_000, 0), 1);
        var viewport = new CadViewport();
        viewport.SetSize(64, 64);
        viewport.SetView(1, new(0, 64));
        using var host = CreateHost(document, viewport);
        var options = new CadRenderOptions
        {
            ActiveLayoutId = layoutId,
            ActiveOwnerBlockId = document.GetLayout(layoutId).PaperSpaceBlockId,
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false
        };
        host.SetRenderOptions(options);
        host.RenderCacheBuildRequested += (_, _) => { };
        Assert.False(host.IsInitialViewReady);
        WaitUntilVisible(host);
        Assert.True(host.PrepareRenderCacheStep());
        host.Render(CadRenderInvalidation.Full);
        document.GetLayout(layoutId).GetViewport(modelViewId).SetView(
            CadRectD.FromXYWH(8, 8, 48, 48), new(15_000, 0), 1, 0);
        Assert.False(host.IsInitialViewReady);
        WaitUntilVisible(host);
        Assert.True(host.PrepareRenderCacheStep());
        host.Render(CadRenderInvalidation.Full);
        using var reference = CreateHost(document, viewport);
        reference.SetRenderOptions(options);
        reference.RebuildAll(document);
        reference.Render(CadRenderInvalidation.Full);
        var pixels = host.CaptureBackBufferPixels();
        Assert.Equal(reference.CaptureBackBufferPixels(), pixels);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index =>
            pixels[index * 4 + 2] > 200 && pixels[index * 4 + 1] < 100);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    public void TileReplayAndWideStrokeEditsMatchImmediatePixels(double lineWeight)
    {
        var document = CadDocument.Create("Wide cached stroke");
        for (var index = 0; index < 1_024; index++)
            document.AddLine(new(20_000 + index, 0), new(20_000 + index, 20));
        var wide = document.AddPolyline([new(-40, -40), new(40, 40), new(-40, 20)]);
        wide.SetLineWeight(new CadLineWeight(lineWeight));
        document.AddLine(new(590, -20), new(610, 20));
        var viewport = new CadViewport();
        viewport.SetSize(64, 64);
        viewport.SetView(1, new(32, 32));
        using var host = CreateHost(document, viewport);
        using var reference = CreateHost(document, viewport);
        host.RebuildAll(document);
        reference.RebuildAll(document);
        var watch = Stopwatch.StartNew();
        while (host.PrepareRenderCacheStep())
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The tile cache did not become ready.");
        host.Render(CadRenderInvalidation.Full);
        reference.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.TileReplayCount > 0);
        Assert.Equal(reference.CaptureBackBufferPixels(), host.CaptureBackBufferPixels());

        wide.ReplacePoints([new(-40, 40), new(40, -40), new(-40, -20)]);
        var changes = CadDocumentChangeSet.ForEntities([wide.Id], CadEntityChangeKind.Geometry);
        host.ApplyChanges(document, changes);
        reference.ApplyChanges(document, changes);
        while (host.PrepareRenderCacheStep())
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The edited tile cache did not become ready.");
        host.Render(CadRenderInvalidation.Full);
        reference.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.TileReplayCount > 0);
        Assert.Equal(reference.CaptureBackBufferPixels(), host.CaptureBackBufferPixels());

        viewport.PanScreen(new(-600, 0));
        host.Render(CadRenderInvalidation.Full);
        reference.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.TileReplayCount > 0);
        Assert.Equal(0, host.RenderStatistics.FallbackEntityCount);
        Assert.Equal(reference.CaptureBackBufferPixels(), host.CaptureBackBufferPixels());
        Assert.Contains(host.CaptureBackBufferPixels().Where((_, index) => index % 4 != 3), value => value > 0);
    }

    private static Direct2DImageRenderHost CreateHost(CadDocument document, CadViewport viewport)
    {
        var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new RecordingImageSource(64, 64));
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        host.SetScene(document, viewport);
        return host;
    }

    private static void WaitUntilVisible(Direct2DImageRenderHost host)
    {
        var watch = Stopwatch.StartNew();
        while (!host.IsInitialViewReady)
        {
            host.PrepareRenderCacheStep();
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The current view did not become ready.");
            Thread.Yield();
        }
    }

    private static Direct2DPreparedGeometry WaitForNext(Direct2DGeometryPreparationService service)
    {
        Direct2DPreparedGeometry? result = null;
        Assert.True(SpinWait.SpinUntil(() => service.TryTakeNext(out result), TimeSpan.FromSeconds(5)));
        return Assert.IsType<Direct2DPreparedGeometry>(result);
    }

    private sealed class RecordingImageSource(int width, int height) : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = width;
        public int SurfaceHeight { get; private set; } = height;
        public int PresentCount { get; private set; }
        public void SetSize(int width, int height) { SurfaceWidth = width; SurfaceHeight = height; }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
        { presentAction(); PresentCount++; }
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
    }
}
