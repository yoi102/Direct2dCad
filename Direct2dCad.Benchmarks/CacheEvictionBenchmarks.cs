using BenchmarkDotNet.Attributes;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class CacheEvictionBenchmarks
{
    private Entry[] _entries = [];
    private readonly CacheEvictionQueue<Entry> _queue = new();

    [Params(128, 1024)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _entries = Enumerable.Range(0, EntryCount)
            .Select(i => new Entry(i, random.Next(EntryCount), i % 8 == 0))
            .ToArray();
        if (SortAndMaterialize() != ReusableEvictionQueue())
            throw new InvalidOperationException("Eviction order changed.");
    }

    [Benchmark(Baseline = true)]
    public int SortAndMaterialize()
    {
        var candidates = _entries.Where(entry => !entry.IsProtected)
            .OrderBy(entry => entry.LastUsed).ToArray();
        var checksum = 0;
        for (var i = 0; i < 8; i++)
            checksum = unchecked(checksum * 31 + candidates[i].Id);
        return checksum;
    }

    [Benchmark]
    public int ReusableEvictionQueue()
    {
        try
        {
            foreach (var entry in _entries)
            {
                if (!entry.IsProtected)
                    _queue.Add(entry, entry.LastUsed);
            }
            var checksum = 0;
            for (var i = 0; i < 8 && _queue.TryTake(out var entry); i++)
                checksum = unchecked(checksum * 31 + entry.Id);
            return checksum;
        }
        finally
        {
            _queue.Clear();
        }
    }

    private sealed record Entry(int Id, long LastUsed, bool IsProtected);
}
