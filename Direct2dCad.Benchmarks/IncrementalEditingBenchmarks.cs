using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class BlockDependencyUpdateBenchmarks
{
    private CadDocument _document = null!;
    private CadLine _child = null!;
    private EntityId[] _changed = null!;
    private bool _moved;

    [Params(20_000)] public int ReferenceCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = CadDocument.Create("Dependency update");
        for (var i = 0; i < 100; i++)
        {
            var block = _document.CreateBlockDefinition($"Block{i}", CadPointD.Origin);
            var child = _document.AddLine(new(0, 0), new(10, 10));
            _document.MoveEntityToBlock(child.Id, block);
            if (i == 0) _child = child;
            for (var j = 0; j < ReferenceCount / 100; j++)
                _document.AddBlockReference(block, new(i * 20, j * 20));
        }
        _changed = [_child.Id];
        _document.RefreshBlockReferenceBounds();
    }

    [Benchmark(Baseline = true)]
    public int RefreshAll()
    {
        Move();
        return _document.RefreshBlockReferenceBounds().Count;
    }

    [Benchmark]
    public int RefreshAffected()
    {
        Move();
        return _document.RefreshAffectedBlockReferenceBounds(_changed).Count;
    }

    private void Move()
    {
        _moved = !_moved;
        _child.SetGeometry(new(_moved ? -5 : 0, 0), new(10, 10));
    }
}

[CadBenchmark]
public class SelectionGeometryUpdateBenchmarks
{
    private CadDocument _document = null!;
    private CadLine _line = null!;
    private EntityId[] _ids = null!;
    private EntityId[] _changed = null!;
    private readonly CadHandleSceneBuilder _builder = new();
    private readonly CadHandleSceneBuildBuffer _buffer = new();
    private readonly CadHandleScene _scene = new();
    private bool _moved;

    [Params(20_000)] public int SelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = CadDocument.Create("Selection update");
        _ids = Enumerable.Range(0, SelectionCount).Select(i =>
            _document.AddLine(new(i, 0), new(i + 1, 1)).Id).ToArray();
        _line = (CadLine)_document.GetEntity(_ids[0]);
        _changed = [_line.Id];
        _scene.Replace(_builder.BuildSelectionHandles(_document, _ids, _buffer));
        UpdateChangedGeometry();
    }

    [Benchmark(Baseline = true)]
    public CadRectD RebuildSelection()
    {
        Move();
        _scene.Replace(_builder.BuildSelectionHandles(_document, _ids, _buffer, _scene));
        return _scene.SelectionWorldBounds;
    }

    [Benchmark]
    public CadRectD UpdateChangedGeometry()
    {
        Move();
        if (!_scene.TryUpdateGeometry(_document, _changed))
            throw new InvalidOperationException("Incremental selection update was not used.");
        return _scene.SelectionWorldBounds;
    }

    private void Move()
    {
        _moved = !_moved;
        _line.SetGeometry(new(_moved ? -100 : 0, 0), new(1, 1));
    }
}
