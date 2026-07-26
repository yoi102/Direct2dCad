using BenchmarkDotNet.Attributes;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class Direct2DResourceUpdateBenchmarks
{
    private BenchmarkDocumentData _data = null!;
    private BenchmarkRenderSession _session = null!;
    private CadDocumentChangeSet _singleChange = null!;
    private CadDocumentChangeSet _hundredChanges = null!;

    [Params(BenchmarkDocumentKind.Lines, BenchmarkDocumentKind.Mixed)]
    public BenchmarkDocumentKind DocumentKind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkDocumentFactory.Create(20_000, DocumentKind);
        _session = new BenchmarkRenderSession(_data);
        _session.WarmUp(1);

        _singleChange = CadDocumentChangeSet.ForEntity(
            _data.EntityIds[0],
            CadEntityChangeKind.Geometry);
        _hundredChanges = CadDocumentChangeSet.ForEntities(
            _data.EntityIds.Take(100),
            CadEntityChangeKind.Geometry);
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ResourceUpdate")]
    public void ApplySingleGeometryChange() =>
        _session.RenderHost.ApplyChanges(_data.Document, _singleChange);

    [Benchmark]
    [BenchmarkCategory("ResourceUpdate")]
    public void ApplyHundredGeometryChanges() =>
        _session.RenderHost.ApplyChanges(_data.Document, _hundredChanges);
}
