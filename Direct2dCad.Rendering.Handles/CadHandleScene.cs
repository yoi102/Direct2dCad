namespace Direct2dCad.Rendering.Handles;

public sealed class CadHandleScene
{
    private readonly List<CadHandleItem> _items = [];

    public IReadOnlyList<CadHandleItem> Items => _items;
    public bool IsEmpty => _items.Count == 0;

    public void Replace(IEnumerable<CadHandleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items.Where(x => x is not null));
    }

    public void Clear() => _items.Clear();
}
