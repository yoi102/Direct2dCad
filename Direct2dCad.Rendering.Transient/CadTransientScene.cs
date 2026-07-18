namespace Direct2dCad.Rendering.Transient;

public sealed class CadTransientScene
{
    private readonly List<CadTransientItem> _items = [];

    public IReadOnlyList<CadTransientItem> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    public long Version { get; private set; }

    public void Replace(IEnumerable<CadTransientItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items.Where(x => x is not null));
        unchecked
        {
            Version++;
        }
    }

    public void Clear()
    {
        _items.Clear();
        unchecked
        {
            Version++;
        }
    }
}
