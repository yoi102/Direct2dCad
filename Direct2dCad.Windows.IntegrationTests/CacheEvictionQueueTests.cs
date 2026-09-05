using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class CacheEvictionQueueTests
{
    [Fact]
    public void TakesOldestFirstAndPreservesInputOrderForEqualTimestamps()
    {
        var queue = new CacheEvictionQueue<string>();
        queue.Add("newest", 30);
        queue.Add("first", 10);
        queue.Add("second", 10);
        queue.Add("middle", 20);
        var actual = new List<string>();
        while (queue.TryTake(out var item))
            actual.Add(item);
        Assert.Equal(["first", "second", "middle", "newest"], actual);
    }

    [Fact]
    public void ClearDropsRemainingCandidatesBeforeReuse()
    {
        var queue = new CacheEvictionQueue<object>();
        queue.Add(new object(), 0);
        queue.Add(new object(), 1);
        Assert.True(queue.TryTake(out _));
        queue.Clear();
        Assert.False(queue.TryTake(out _));
        var current = new object();
        queue.Add(current, 10);
        Assert.True(queue.TryTake(out var actual));
        Assert.Same(current, actual);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public void RepeatedEvictionPassesReuseManagedStorage()
    {
        var queue = new CacheEvictionQueue<int>();
        FillAndTrim(queue);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var pass = 0; pass < 100; pass++)
            FillAndTrim(queue);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void FillAndTrim(CacheEvictionQueue<int> queue)
    {
        for (var i = 0; i < 256; i++)
            queue.Add(i, 256 - i);
        for (var i = 0; i < 8; i++)
            queue.TryTake(out _);
        queue.Clear();
    }
}
