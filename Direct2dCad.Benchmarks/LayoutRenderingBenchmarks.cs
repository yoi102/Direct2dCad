using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class LayoutRenderingBenchmarks
{
    private BenchmarkRenderSession _session = null!;

    [Params(false, true)]
    public bool ActiveViewport { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var layoutData = BenchmarkDocumentFactory.CreateLayoutDocument();
        _session = new BenchmarkRenderSession(
            layoutData.Data,
            activeOwnerBlockId: layoutData.PaperSpaceBlockId,
            activeLayoutId: layoutData.LayoutId,
            activeLayoutViewportId: ActiveViewport ? layoutData.ViewportId : null);
        _session.WarmUp();
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Layout", "WarmFrame")]
    public long FullFrameWarmCache()
    {
        _session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return _session.CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Layout", "ResourceBuild")]
    public long RebuildResourcesAndFullFrame()
    {
        _session.RenderHost.RebuildAll(_session.Data.Document);
        _session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return _session.CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("Layout", "FirstFrame")]
    public long ReattachSurfaceAndFirstFrame() =>
        _session.ReattachSurfaceAndRenderFirstFrame();
}
