using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class ComplexSceneRenderingBenchmarks
{
    private BenchmarkRenderSession _session = null!;

    [Params(
        BenchmarkComplexDocumentKind.Text,
        BenchmarkComplexDocumentKind.Hatch,
        BenchmarkComplexDocumentKind.Blocks,
        BenchmarkComplexDocumentKind.Images)]
    public BenchmarkComplexDocumentKind DocumentKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var data = BenchmarkDocumentFactory.CreateComplex(DocumentKind);
        _session = new BenchmarkRenderSession(data);
        _session.WarmUp();
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ComplexScene", "WarmFrame")]
    public long FullFrameWarmCache()
    {
        _session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return _session.CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("ComplexScene", "ResourceBuild")]
    public long RebuildResourcesAndFullFrame()
    {
        _session.RenderHost.RebuildAll(_session.Data.Document);
        _session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return _session.CaptureFrameChecksum();
    }

    [Benchmark]
    [BenchmarkCategory("ComplexScene", "FirstFrame")]
    public long ReattachSurfaceAndFirstFrame() =>
        _session.ReattachSurfaceAndRenderFirstFrame();
}
