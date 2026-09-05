using BenchmarkDotNet.Attributes;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class SelectionAvailabilityBenchmarks
{
    private CadEditor _editor = null!;
    private readonly CadSelectionAvailabilityCache _cache = new();

    [Params(512, 20_000)]
    public int SelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var data = BenchmarkDocumentFactory.Create(SelectionCount, BenchmarkDocumentKind.Mixed);
        _editor = new CadEditor(data.Document);
        _editor.Selection.Replace(data.EntityIds);
        _cache.Get(_editor);
    }

    [Benchmark(Baseline = true)]
    public bool ScanSelectedEntities() => _editor.Selection.Count > 0 &&
        _editor.Selection.EntityIds.All(id =>
            _editor.Document.TryGetEntity(id, out var entity) && entity is not null &&
            entity.OwnerBlockId == _editor.ActiveOwnerBlockId &&
            CadEntityAccessPolicy.IsEditable(_editor.Document, entity)) &&
        _editor.Document.Layers.Values.Any(layer => CadEntityAccessPolicy.CanAddToLayer(_editor.Document, layer.Id));

    [Benchmark]
    public bool ReadCachedAvailability() => _cache.Get(_editor).CanCreateBlock;
}
