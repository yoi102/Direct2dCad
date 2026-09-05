using BenchmarkDotNet.Attributes;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class PreparationSnapshotBenchmarks
{
    private EntityPreparationSnapshot[] _source = null!;
    private EntityPreparationSnapshots _pages = null!;
    private Dictionary<int, EntityPreparationSnapshot> _updates = null!;

    [Params(20_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var document = CadDocument.Create("Snapshot benchmark");
        _source = Enumerable.Range(0, EntityCount).Select(i =>
        {
            var entity = document.AddLine(new CadPointD(i, 0), new CadPointD(i, 10));
            return new EntityPreparationSnapshot(entity, 0, 0, i, entity.Bounds, false, true, 1, null);
        }).ToArray();
        _pages = new EntityPreparationSnapshots(_source);
        _updates = new Dictionary<int, EntityPreparationSnapshot>
        {
            [EntityCount / 2] = _source[EntityCount / 2] with { Bounds = CadRectD.FromXYWH(10, 10, 10, 10) }
        };
    }

    [Benchmark(Baseline = true)]
    public object FullCopy()
    {
        var copy = (EntityPreparationSnapshot[])_source.Clone();
        foreach (var (index, value) in _updates)
            copy[index] = value;
        return copy;
    }

    [Benchmark]
    public object CopyChangedPages() => _pages.WithUpdates(_updates);
}
