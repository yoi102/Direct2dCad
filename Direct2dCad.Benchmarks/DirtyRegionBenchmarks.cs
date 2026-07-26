using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class DirtyRegionBenchmarks
{
    private CadScreenRect[] _dirtyRects = null!;

    [Params(8, 32, 128)]
    public int DirtyRectCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dirtyRects = new CadScreenRect[DirtyRectCount];
        for (var index = 0; index < _dirtyRects.Length; index++)
        {
            var x = index % 16 * 90;
            var y = index / 16 * 70;
            _dirtyRects[index] = new CadScreenRect(x, y, 24, 20);
        }
    }

    [Benchmark]
    [BenchmarkCategory("DirtyRegion")]
    public int BuildFromBatch() =>
        CadRenderInvalidation.FromScreenRects(_dirtyRects).DirtyScreenRects.Count;

    [Benchmark]
    [BenchmarkCategory("DirtyRegion")]
    public int UnionIncrementally()
    {
        var invalidation = CadRenderInvalidation.Empty;
        foreach (var rect in _dirtyRects)
            invalidation = invalidation.Union(CadRenderInvalidation.FromScreenRect(rect));
        return invalidation.DirtyScreenRects.Count;
    }
}
