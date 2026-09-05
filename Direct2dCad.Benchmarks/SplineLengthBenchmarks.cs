using BenchmarkDotNet.Attributes;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class SplineLengthBenchmarks
{
    private CadSpline _spline = null!;

    [Params(32, 512)]
    public int FitPointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _spline = CadDocument.Create("Length").AddSpline(Enumerable.Range(0, FitPointCount)
            .Select(i => new CadPointD(i, Math.Sin(i * 0.1) * 10)));
        _ = _spline.Length;
    }

    [Benchmark(Baseline = true)]
    public double FlattenAndMeasure()
    {
        using var points = _spline.EnumerateFlattenedPoints(20).GetEnumerator();
        if (!points.MoveNext())
            return 0;
        var previous = points.Current;
        var length = 0.0;
        while (points.MoveNext())
        {
            length += previous.DistanceTo(points.Current);
            previous = points.Current;
        }
        return length;
    }

    [Benchmark]
    public double CachedLength() => _spline.Length;
}
