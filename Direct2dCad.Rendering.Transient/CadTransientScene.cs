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
        if (_items.Count == 0 &&
            items.TryGetNonEnumeratedCount(out var itemCount) &&
            itemCount == 0)
        {
            return;
        }

        _items.Clear();
        foreach (var item in items)
        {
            if (item is not null)
                _items.Add(item);
        }
        unchecked
        {
            Version++;
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        unchecked
        {
            Version++;
        }
    }
}
