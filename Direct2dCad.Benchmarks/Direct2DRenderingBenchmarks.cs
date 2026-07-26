using BenchmarkDotNet.Attributes;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class Direct2DRenderingBenchmarks
{
    private BenchmarkDocumentData _data = null!;
    private BenchmarkRenderSession _session = null!;
    private CadRenderInvalidation _partialInvalidation = null!;
    private CadRenderInvalidation _multipleDirtyRegions = null!;
    private bool _panForward;
    private bool _zoomIn;

    [Params(20_000)]
    public int EntityCount { get; set; }

    [Params(BenchmarkDocumentKind.Lines, BenchmarkDocumentKind.Mixed)]
    public BenchmarkDocumentKind DocumentKind { get; set; }

    [Params(false, true)]
    public bool LevelOfDetail { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkDocumentFactory.Create(EntityCount, DocumentKind);
        _session = new BenchmarkRenderSession(_data, LevelOfDetail);
        _session.WarmUp();

        _partialInvalidation = CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(680, 390, 240, 120));
        _multipleDirtyRegions = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(80, 80, 120, 100),
            new CadScreenRect(650, 180, 140, 120),
            new CadScreenRect(1180, 650, 160, 110),
            new CadScreenRect(360, 690, 110, 90)
        ]);
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Frame")]
    public long FullFrameWarmCache()
    {
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Frame")]
    public long PartialFrameSingleRegion()
    {
        _session.RenderHost.Render(_partialInvalidation, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Frame")]
    public long PartialFrameFourRegions()
    {
        _session.RenderHost.Render(_multipleDirtyRegions, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Frame")]
    public long RestoreCachedBaseScene()
    {
        _session.RenderHost.Render(_partialInvalidation, baseSceneChanged: false);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("ResourceBuild")]
    public long RebuildResourcesAndFullFrame()
    {
        _session.RenderHost.RebuildAll(_data.Document);
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("FirstFrame")]
    public long ReattachSurfaceAndFirstFrame() =>
        _session.ReattachSurfaceAndRenderFirstFrame();

    [Benchmark]
    [BenchmarkCategory("Interaction")]
    public long PanAndFullRender()
    {
        _panForward = !_panForward;
        _session.Viewport.PanScreen(new CadVectorD(_panForward ? 16 : -16, 0));
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Interaction")]
    public long ZoomAndFullRender()
    {
        _zoomIn = !_zoomIn;
        _session.Viewport.ZoomAt(
            new CadPointD(
                BenchmarkRenderSession.SurfaceWidth / 2.0,
                BenchmarkRenderSession.SurfaceHeight / 2.0),
            _zoomIn ? 1.02 : 1.0 / 1.02);
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("InteractionPreview")]
    public long ZoomSnapshotPreview()
    {
        _session.RenderHost.BeginViewportInteraction();
        _zoomIn = !_zoomIn;
        _session.Viewport.ZoomAt(
            new CadPointD(
                BenchmarkRenderSession.SurfaceWidth / 2.0,
                BenchmarkRenderSession.SurfaceHeight / 2.0),
            _zoomIn ? 1.02 : 1.0 / 1.02);
        var rendered = _session.RenderHost.RenderViewportInteractionPreview();
        _session.RenderHost.EndViewportInteraction();
        return rendered ? CaptureFrameChecksum() : -1;
    }

    [Benchmark]
    [BenchmarkCategory("SustainedInteraction", "Pan")]
    public long PanSequenceSixteenFrames()
    {
        const int frameCount = 16;
        const double delta = 4.0;
        for (var index = 0; index < frameCount; index++)
        {
            _session.Viewport.PanScreen(new CadVectorD(delta, 0));
            _session.RenderHost.Render(
                CadRenderInvalidation.Full,
                baseSceneChanged: true);
        }

        var checksum = CaptureFrameChecksum();
        _session.Viewport.PanScreen(new CadVectorD(-delta * frameCount, 0));
        return checksum;
    }

    [Benchmark]
    [BenchmarkCategory("SustainedInteraction", "Zoom")]
    public long ZoomSequenceSixteenFrames()
    {
        const int halfFrameCount = 8;
        const double zoomFactor = 1.01;
        var center = new CadPointD(
            BenchmarkRenderSession.SurfaceWidth / 2.0,
            BenchmarkRenderSession.SurfaceHeight / 2.0);
        for (var index = 0; index < halfFrameCount; index++)
        {
            _session.Viewport.ZoomAt(center, zoomFactor);
            _session.RenderHost.Render(
                CadRenderInvalidation.Full,
                baseSceneChanged: true);
        }

        for (var index = 0; index < halfFrameCount; index++)
        {
            _session.Viewport.ZoomAt(center, 1.0 / zoomFactor);
            _session.RenderHost.Render(
                CadRenderInvalidation.Full,
                baseSceneChanged: true);
        }

        return CaptureFrameChecksum();
    }

    private long CaptureFrameChecksum() => _session.CaptureFrameChecksum();
}
