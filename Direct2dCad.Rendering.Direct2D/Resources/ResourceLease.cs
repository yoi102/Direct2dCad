namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class ResourceLease<T>(T resource, Action release) : IDisposable where T : IDisposable
{
    private Action? _release = release;

    public T Resource { get; } = resource;

    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

internal sealed class KeyedResourceLease<TResource, TKey>(
    TResource resource,
    TKey key,
    Action<TKey> release) : IDisposable
    where TResource : IDisposable
{
    private int _released;

    public TResource Resource { get; } = resource;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            release(key);
    }
}
