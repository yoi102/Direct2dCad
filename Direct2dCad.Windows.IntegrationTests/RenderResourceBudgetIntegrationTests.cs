using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class RenderResourceBudgetIntegrationTests
{
    [Fact]
    public void GridAndTransientCommandListsParticipateInTheBudget()
    {
        var document = CadDocument.Create("Grid and transient cache");
        document.ViewSettings.Grid.Type = CadGridType.Dots;
        var scene = CreateTransientLines(document);
        var viewport = new CadViewport();
        viewport.SetSize(96, 96);
        viewport.SetView(1, new(48, 48));
        var options = new CadRenderOptions { DrawGrid = true, DrawOrigin = false, DrawGripHandles = false };
        using var reference = CreateHost(document, viewport, new CadRenderResourceBudget());
        reference.SetTransientScene(scene);
        reference.SetRenderOptions(options);
        PrepareAll(reference);
        reference.Render();
        Assert.True(reference.RenderStatistics.GridTileCacheBytes > 0);
        Assert.True(reference.RenderStatistics.TransientGroupCacheBytes > 0);

        using var constrained = CreateHost(document, viewport, new CadRenderResourceBudget(1_024, 1_024));
        constrained.SetTransientScene(scene);
        constrained.SetRenderOptions(options);
        PrepareAll(constrained);
        constrained.Render();
        Assert.Equal(reference.CaptureBackBufferPixels(), constrained.CaptureBackBufferPixels());
        Assert.InRange(constrained.ResourceStatistics.EstimatedRetainedCacheBytes, 0, 1_024);
        Assert.Equal(0, constrained.RenderStatistics.GridTileCacheBytes);
        Assert.Equal(0, constrained.RenderStatistics.TransientGroupCacheBytes);
    }

    [Fact]
    public void ANewSceneCanBuildCachesAfterPreviousSceneExceededItsQuota()
    {
        var large = CadDocument.Create("Oversized image");
        large.AddImage(CadRectD.FromXYWH(-20, -20, 40, 40), 512, 512, 2048, new byte[512 * 512 * 4]);
        var viewport = new CadViewport();
        viewport.SetSize(96, 96);
        viewport.SetView(1, new(48, 48));
        using var host = CreateHost(large, viewport, new CadRenderResourceBudget(128 * 1024, 128 * 1024));
        host.Render();
        Assert.True(host.RenderStatistics.GpuCacheEvictionCount > 0);
        PrepareAll(host);

        var small = CadDocument.Create("Small new scene");
        var transient = CreateTransientLines(small);
        host.SetScene(small, viewport, prepareResourcesInBackground: false);
        host.SetTransientScene(transient);
        PrepareAll(host);
        host.Render();
        Assert.True(host.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.InRange(host.ResourceStatistics.EstimatedRetainedCacheBytes, 1, 128 * 1024);
    }

    private static CadTransientScene CreateTransientLines(CadDocument document)
    {
        var references = new List<CadTransientItem>();
        for (var index = 0; index < 256; index++)
        {
            var x = -40 + index % 16 * 5;
            var y = -40 + index / 16 * 5;
            var line = document.AddLine(new(x, y), new(x + 2, y + 2));
            references.Add(new CadTransientEntityReference(line.Id, new(0, 0), CadTransientStyle.PastePreview));
        }
        var scene = new CadTransientScene();
        scene.Replace([new CadTransientGroup(references, CadMatrixD.Identity)]);
        return scene;
    }

    private static void PrepareAll(Direct2DImageRenderHost host)
    {
        var pending = true;
        for (var step = 0; step < 500 && pending; step++)
        {
            pending = host.PrepareRenderCacheStep();
            if (pending) Thread.Sleep(1);
        }
        Assert.False(pending);
    }

    [Theory]
    [InlineData(false, CadParallelRenderingMode.SharedDeviceContexts)]
    [InlineData(true, CadParallelRenderingMode.SharedDeviceContexts)]
    [InlineData(true, CadParallelRenderingMode.MultipleDevices)]
    public void ImageEvictionPreservesRepeatedFramePixelsAndBoundsAllWorkerCaches(
        bool parallel, CadParallelRenderingMode mode)
    {
        var document = CadDocument.Create("Cache pressure");
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i % 251); pixels[i + 1] = 170; pixels[i + 2] = 70; pixels[i + 3] = 255; }
        document.AddImage(CadRectD.FromXYWH(-35, -20, 30, 40), 64, 64, 256, pixels);
        document.AddImage(CadRectD.FromXYWH(5, -20, 30, 40), 64, 64, 256, pixels);
        for (var i = 0; i < 12; i++) document.AddLine(new(-40, -30 + i * 5), new(40, -30 + i * 5));
        var viewport = new CadViewport();
        viewport.SetSize(96, 96);
        viewport.SetView(1, new(48, 48));
        var lowBudget = new CadRenderResourceBudget(4_096, 4_096);
        using var reference = CreateHost(document, viewport, new CadRenderResourceBudget());
        reference.Render(CadRenderInvalidation.Full);
        var expected = reference.CaptureBackBufferPixels();
        using var constrained = CreateHost(document, viewport, lowBudget);
        constrained.SetRenderOptions(new()
        {
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsParallelRenderingEnabled = parallel, ParallelRenderingMode = mode,
            ParallelRenderingWorkerCount = 2, ParallelRenderingEntityThreshold = 2
        });
        for (var frame = 0; frame < 3; frame++)
        {
            constrained.Render(CadRenderInvalidation.Full);
            Assert.Equal(expected, constrained.CaptureBackBufferPixels());
            Assert.InRange(constrained.ResourceStatistics.EstimatedRetainedCacheBytes, 0, 4_096);
            Assert.Equal(constrained.ResourceStatistics.EstimatedRetainedCacheBytes,
                lowBudget.Statistics.EstimatedRetainedCacheBytes);
        }
        if (parallel)
            Assert.Equal(3, constrained.ResourceStatistics.RendererCount);
        var pending = true;
        for (var step = 0; step < 20 && pending; step++) pending = constrained.PrepareRenderCacheStep();
        Assert.False(pending); // Pressure must not spin forever rebuilding the same cache.
        constrained.Dispose();
        Assert.Equal(0, lowBudget.Statistics.EstimatedRetainedCacheBytes);
        Assert.Equal(0, lowBudget.Statistics.DocumentCount);
    }

    private static Direct2DImageRenderHost CreateHost(CadDocument document, CadViewport viewport,
        CadRenderResourceBudget budget)
    {
        var host = new Direct2DImageRenderHost(null, budget);
        host.AttachImageSource(new ImageSource());
        host.SetSize(96, 96);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        return host;
    }

    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth => 96;
        public int SurfaceHeight => 96;
        public void SetSize(int width, int height) { }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
    }
}
