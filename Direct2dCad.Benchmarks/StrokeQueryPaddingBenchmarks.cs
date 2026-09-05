using BenchmarkDotNet.Attributes;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class StrokeQueryPaddingBenchmarks
{
    private readonly CadSpatialIndex _index = new();
    private readonly List<EntityId> _candidates = [];
    private CadRectD _queryBounds;
    private ImageSourceDirect2DResource _target = null!;
    private Direct2DStyleResourceCache _styles = null!;
    private Direct2DTextFormatResourceCache _text = null!;
    private Direct2DResourceCache _resources = null!;

    [Params(0.25, 20)] public double OutlierLineWeight { get; set; }
    [Params(true, false)] public bool ScreenConstant { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var data = BenchmarkDocumentFactory.Create(20_000, BenchmarkDocumentKind.Lines);
        var outlier = data.Document.AddLine(new(-10000, -10000), new(-9990, -9990));
        outlier.SetLineWeight(new CadLineWeight(OutlierLineWeight));
        _index.Rebuild(data.Document);
        _target = new ImageSourceDirect2DResource();
        _styles = new Direct2DStyleResourceCache();
        _text = new Direct2DTextFormatResourceCache();
        _styles.Reset(_target.Factory, _target.Context);
        _text.Reset(_target.DwriteFactory);
        _resources = new Direct2DResourceCache(_styles, _text, new Direct2DRenderStatisticsCollector(),
            _target.Factory, _target.DwriteFactory, _target.Context);
        _resources.RebuildAll(data.Document);
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(4, new(160, 120));
        var padding = Direct2DEntityVisibility.ResolveBroadPhasePadding(_resources, viewport,
            new CadRenderOptions { KeepStrokeWidthScreenConstant = ScreenConstant });
        _queryBounds = viewport.VisibleWorldBounds.Inflate(padding);
        Console.WriteLine($"StrokeQuery: weight={OutlierLineWeight}, screenConstant={ScreenConstant}, padding={padding}, candidates={QueryCandidates()}");
    }

    [Benchmark]
    public int QueryCandidates()
    {
        _candidates.Clear();
        _index.Query(BlockId.ModelSpace, _queryBounds, _candidates);
        return _candidates.Count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _resources?.Dispose();
        _styles?.Dispose();
        _text?.Dispose();
        _target?.Dispose();
    }
}
