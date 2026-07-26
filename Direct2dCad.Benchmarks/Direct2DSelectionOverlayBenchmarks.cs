using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class Direct2DSelectionOverlayBenchmarks
{
    private readonly CadHandleSceneBuilder _builder = new();
    private readonly CadHandleSceneBuildBuffer _buildBuffer = new();
    private readonly CadHandleScene _handleScene = new();
    private BenchmarkDocumentData _data = null!;
    private EntityId[] _selectedEntityIds = null!;
    private BenchmarkRenderSession _session = null!;

    [Params(512, 20_000)]
    public int SelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkDocumentFactory.Create(20_000, BenchmarkDocumentKind.Mixed);
        _selectedEntityIds = _data.EntityIds.Take(SelectionCount).ToArray();
        _session = new BenchmarkRenderSession(_data);
        _session.RenderHost.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = true,
            EntityBoundsQueryInto = (ownerBlockId, bounds, results) =>
                _session.SpatialIndex.Query(ownerBlockId, bounds, results)
        });
        _session.RenderHost.RebuildAll(_data.Document);
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        var items = _builder.BuildSelectionHandles(
            _data.Document,
            _selectedEntityIds,
            _buildBuffer,
            _handleScene);
        _handleScene.Replace(items);
        _session.RenderHost.SetHandleScene(_handleScene);
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark]
    [BenchmarkCategory("SelectionGpuOverlay")]
    public long RenderWarmSelectionOverlay()
    {
        _session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var statistics = _session.RenderHost.RenderStatistics;
        return statistics.SelectionEntityCount +
               statistics.SelectionCommandListReplayCount +
               statistics.LargeSelectionFallbackCount +
               _session.ImageSource.PresentCount;
    }
}
