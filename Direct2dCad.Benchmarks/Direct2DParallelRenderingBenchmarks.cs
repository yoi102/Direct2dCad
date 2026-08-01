using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

public enum ParallelRenderingBenchmarkMode
{
    Disabled,
    MultipleDevices,
    SharedDeviceContexts
}

public readonly record struct ParallelRenderingBenchmarkConfiguration(
    ParallelRenderingBenchmarkMode Mode,
    int WorkerCount)
{
    public override string ToString() => Mode == ParallelRenderingBenchmarkMode.Disabled
        ? nameof(ParallelRenderingBenchmarkMode.Disabled)
        : $"{Mode}-{WorkerCount}";
}

[CadBenchmark]
public class Direct2DParallelRenderingBenchmarks
{
    private BenchmarkRenderSession _session = null!;

    [Params(20_000)]
    public int EntityCount { get; set; }

    public IEnumerable<ParallelRenderingBenchmarkConfiguration> ConfigurationOptions =>
    [
        new(ParallelRenderingBenchmarkMode.Disabled, 2),
        new(ParallelRenderingBenchmarkMode.MultipleDevices, 2),
        new(ParallelRenderingBenchmarkMode.MultipleDevices, 4),
        new(ParallelRenderingBenchmarkMode.SharedDeviceContexts, 2),
        new(ParallelRenderingBenchmarkMode.SharedDeviceContexts, 4)
    ];

    [ParamsSource(nameof(ConfigurationOptions))]
    public ParallelRenderingBenchmarkConfiguration Configuration { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var mode = Configuration.Mode switch
        {
            ParallelRenderingBenchmarkMode.MultipleDevices =>
                CadParallelRenderingMode.MultipleDevices,
            ParallelRenderingBenchmarkMode.SharedDeviceContexts =>
                CadParallelRenderingMode.SharedDeviceContexts,
            _ => (CadParallelRenderingMode?)null
        };
        _session = new BenchmarkRenderSession(
            BenchmarkDocumentFactory.Create(EntityCount, BenchmarkDocumentKind.Mixed),
            parallelRenderingMode: mode,
            parallelWorkerCount: Configuration.WorkerCount);
        _session.WarmUp();
    }

    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    [Benchmark]
    [BenchmarkCategory("ParallelRendering")]
    public long FullFrameWarmCache()
    {
        _session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return _session.CaptureFrameChecksum();
    }
}
