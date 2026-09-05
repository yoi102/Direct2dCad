using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class DirtyRegionBatchBenchmarks
{
    private CadScreenRect[] _rectangles = null!;

    [Params(512, 20_000)]
    public int RectangleCount { get; set; }

    [GlobalSetup]
    public void Setup() => _rectangles = Enumerable.Range(0, RectangleCount)
        .Select(i => new CadScreenRect((i % 200) * 20, (i / 200) * 20, 2, 2)).ToArray();

    [Benchmark]
    public CadRenderInvalidation NormalizeBulkEdit() =>
        CadRenderInvalidation.FromScreenRects(_rectangles);
}
