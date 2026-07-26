using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class SelectionOverlayBenchmarks
{
    private readonly CadHandleSceneBuilder _builder = new();
    private readonly CadHandleSceneBuildBuffer _buffer = new();
    private BenchmarkDocumentData _data = null!;
    private EntityId[] _selectedEntityIds = null!;

    [Params(1, 512, 20_000)]
    public int SelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkDocumentFactory.Create(20_000, BenchmarkDocumentKind.Mixed);
        _selectedEntityIds = _data.EntityIds.Take(SelectionCount).ToArray();
        _builder.BuildSelectionHandles(_data.Document, _selectedEntityIds, _buffer);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SelectionOverlay")]
    public int BuildWithReusableBuffer() =>
        _builder.BuildSelectionHandles(
            _data.Document,
            _selectedEntityIds,
            _buffer).Count;

    [Benchmark]
    [BenchmarkCategory("SelectionOverlay")]
    public int BuildWithFreshCollections() =>
        _builder.BuildSelectionHandles(_data.Document, _selectedEntityIds).Count;
}
