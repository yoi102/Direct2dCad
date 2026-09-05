using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class OwnerBoundsUpdateBenchmarks
{
    private CadDocument _document = null!;
    private CadLine _line = null!;
    private CadRectD[] _bounds = null!;
    private Direct2DOwnerRenderPacket _packet = null!;
    private bool _expanded;

    [Params(20_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = CadDocument.Create("Owner bounds benchmark");
        var lines = Enumerable.Range(0, EntityCount).Select(i =>
            _document.AddLine(new CadPointD(i, i), new CadPointD(i + 1, i + 1))).ToArray();
        _line = lines[0];
        _bounds = lines.Select(line => line.Bounds).ToArray();
        _packet = new Direct2DOwnerRenderPacket(_document, BlockId.ModelSpace, lines, 0);
    }

    [Benchmark(Baseline = true)]
    public CadRectD FullBoundsScan()
    {
        Move();
        _bounds[0] = _line.Bounds;
        var result = CadRectD.Empty;
        foreach (var bounds in _bounds)
            result = result.Union(bounds);
        return result;
    }

    [Benchmark]
    public CadRectD IncrementalBoundsUpdate()
    {
        Move();
        _packet.TryUpdate(_document, _line.Id, 1);
        return _packet.Bounds;
    }

    private void Move()
    {
        _expanded = !_expanded;
        var value = _expanded ? -100 : 0;
        _line.SetGeometry(new CadPointD(value, value), new CadPointD(value + 1, value + 1));
    }
}
