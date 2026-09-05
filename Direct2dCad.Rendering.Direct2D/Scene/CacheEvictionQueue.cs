namespace Direct2dCad.Rendering.Direct2D.Scene;

// Render-thread-owned scratch storage. Equal timestamps retain enumeration order.
internal sealed class CacheEvictionQueue<T>
{
    private readonly PriorityQueue<T, (long LastUsed, int Order)> _queue = new();
    private int _order;

    public void Add(T item, long lastUsed) => _queue.Enqueue(item, (lastUsed, _order++));

    public bool TryTake(out T item) => _queue.TryDequeue(out item!, out _);

    public void Clear()
    {
        _queue.Clear();
        _order = 0;
    }
}
