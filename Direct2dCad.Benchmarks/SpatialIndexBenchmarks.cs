using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Indexing;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class SpatialIndexBenchmarks
{
    private BenchmarkDocumentData _data = null!;
    private CadSpatialIndex _index = null!;
    private CadRectD _visibleArea;
    private List<EntityId> _queryBuffer = null!;
    private EntityId[] _updatedEntityIds = null!;
    private CadRectD[] _firstBounds = null!;
    private CadRectD[] _secondBounds = null!;
    private bool _useSecondBounds;

    [Params(20_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkDocumentFactory.Create(EntityCount, BenchmarkDocumentKind.Lines);
        _index = new CadSpatialIndex();
        _index.Rebuild(_data.Document);
        _visibleArea = CadRectD.FromXYWH(
            _data.Bounds.MinX + _data.Width * 0.25,
            _data.Bounds.MinY + _data.Height * 0.25,
            _data.Width * 0.5,
            _data.Height * 0.5);
        _queryBuffer = new List<EntityId>(EntityCount / 3);

        var updateCount = Math.Max(1, EntityCount / 100);
        _updatedEntityIds = _data.EntityIds.Take(updateCount).ToArray();
        _firstBounds = new CadRectD[updateCount];
        _secondBounds = new CadRectD[updateCount];
        for (var index = 0; index < updateCount; index++)
        {
            var bounds = _data.Document.Entities[_updatedEntityIds[index]].Bounds;
            _firstBounds[index] = bounds;
            _secondBounds[index] = bounds.Translate(new CadVectorD(0.25, 0.25));
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query")]
    public int QueryVisibleAreaAllocating() =>
        _index.Query(BlockId.ModelSpace, _visibleArea).Count;

    [Benchmark]
    [BenchmarkCategory("Query")]
    public int QueryVisibleAreaIntoReusableBuffer()
    {
        _queryBuffer.Clear();
        _index.Query(BlockId.ModelSpace, _visibleArea, _queryBuffer);
        return _queryBuffer.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Build")]
    public int RebuildIndex()
    {
        var index = new CadSpatialIndex();
        index.Rebuild(_data.Document);
        return index.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Update")]
    public int UpdateOnePercentAndQuery()
    {
        _useSecondBounds = !_useSecondBounds;
        var bounds = _useSecondBounds ? _secondBounds : _firstBounds;
        for (var index = 0; index < _updatedEntityIds.Length; index++)
            _index.Update(_updatedEntityIds[index], BlockId.ModelSpace, bounds[index]);

        _queryBuffer.Clear();
        _index.Query(BlockId.ModelSpace, _visibleArea, _queryBuffer);
        return _queryBuffer.Count;
    }
}
