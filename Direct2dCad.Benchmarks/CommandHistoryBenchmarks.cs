using BenchmarkDotNet.Attributes;
using Direct2dCad.Editor.History;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class CommandHistoryBenchmarks
{
    private readonly CommandHistory<object> _history = new();
    private readonly Stack<(object Command, Guid? BatchId)> _legacyHistory = new();

    [Params(1000, 20_000)]
    public int CommandCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _history.Clear();
        _legacyHistory.Clear();
        for (var i = 0; i < CommandCount; i++)
        {
            var command = new object();
            _history.PushExecuted(command);
            _legacyHistory.Push((command, null));
        }
    }

    [Benchmark(Baseline = true)]
    public object CopyEntireHistory() => _legacyHistory.ToArray();

    [Benchmark]
    public object CaptureStateToken() => _history.CreateUndoSnapshot();
}
