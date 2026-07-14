namespace Direct2dCad.Rendering.Direct2D;

internal sealed class ResourceLease<T>(T resource, Action release) : IDisposable where T : IDisposable
{
    private Action? _release = release;

    public T Resource { get; } = resource;

    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
