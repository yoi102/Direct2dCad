using BenchmarkDotNet.Attributes;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Benchmarks;

// Isolates uncached submission/compositing; it intentionally does not replay scene tiles.
[CadBenchmark]
public class ParallelSubmissionBenchmarks
{
    private ImageSourceDirect2DResource _target = null!;
    private Direct2DSceneRender _single = null!;
    private Direct2DSharedDeviceSceneRenderer _shared = null!;
    private Direct2DMultiDeviceSceneRenderer _multiple = null!;
    private BenchmarkDocumentData _data = null!;
    private CadEntity[] _entities = null!;
    private CadViewport _viewport = null!;
    private CadRenderOptions _options = null!;

    [Params(1280, 3840)] public int Width { get; set; }
    [Params(10, 100)] public int OccupiedAreaPercent { get; set; }
    [Params(ParallelRenderingBenchmarkMode.Disabled, ParallelRenderingBenchmarkMode.SharedDeviceContexts,
        ParallelRenderingBenchmarkMode.MultipleDevices)]
    public ParallelRenderingBenchmarkMode Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var height = Width * 9 / 16;
        _data = BenchmarkDocumentFactory.Create(20_000, BenchmarkDocumentKind.Lines);
        _entities = _data.Document.Entities.Values.ToArray();
        _viewport = BenchmarkDocumentFactory.CreateFittedViewport(_data, Width, height);
        _viewport.SetView(_viewport.Zoom * Math.Sqrt(OccupiedAreaPercent / 100.0), _viewport.Offset);
        _target = new ImageSourceDirect2DResource();
        _target.SetTarget(new BenchmarkImageSource(Width, height));
        _single = new Direct2DSceneRender();
        _single.ResetDeviceResources(_target.Factory, _target.DwriteFactory, _target.Device,
            _target.Context, _data.Document, prepareBackgroundResources: false);
        _shared = new Direct2DSharedDeviceSceneRenderer();
        _multiple = new Direct2DMultiDeviceSceneRenderer();
        _options = new CadRenderOptions
        {
            IsParallelRenderingEnabled = Mode != ParallelRenderingBenchmarkMode.Disabled,
            ParallelRenderingMode = Mode == ParallelRenderingBenchmarkMode.SharedDeviceContexts
                ? CadParallelRenderingMode.SharedDeviceContexts : CadParallelRenderingMode.MultipleDevices,
            ParallelRenderingEntityThreshold = 2,
            ParallelRenderingWorkerCount = 2,
            DrawGrid = false,
            DrawOrigin = false,
            IsLevelOfDetailEnabled = false
        };
        SubmitFrame();
        SubmitFrame();
    }

    [Benchmark]
    public int SubmitFrame()
    {
        IDisposable? lease = null;
        try
        {
            _target.DrawFrame(context =>
            {
                context.Clear(new(0, 0, 0, 0));
                if (Mode == ParallelRenderingBenchmarkMode.Disabled)
                {
                    _single.RenderEntityBatch(_data.Document, _viewport, _options, _entities);
                    return;
                }
                bool drawn;
                if (Mode == ParallelRenderingBenchmarkMode.SharedDeviceContexts)
                    drawn = _shared.TryDraw(_target.D3DDevice!, _target.Factory!, _target.Device!, context,
                        _target.DwriteFactory!, _data.Document, _viewport, _options, _entities,
                        _target.Width, _target.Height, static () => { }, out _);
                else
                {
                    drawn = _multiple.TryDraw(_target.D3DDevice!, context, _target.DwriteFactory!,
                        _data.Document, _viewport, _options, _entities, _target.Width, _target.Height,
                        static () => { }, out var frameLease, out _);
                    lease = frameLease;
                }
                if (!drawn)
                    throw new InvalidOperationException("Parallel benchmark fell back instead of rendering.");
            }, present: false);
        }
        finally
        {
            lease?.Dispose();
        }
        return _entities.Length;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _multiple?.Dispose();
        _shared?.Dispose();
        _single?.Dispose();
        _target?.Dispose();
    }
}
