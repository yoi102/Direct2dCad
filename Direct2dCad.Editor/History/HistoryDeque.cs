using System.Collections;

namespace Direct2dCad.Editor.History;

// Array-backed stack with amortized O(1) removal of expired oldest entries.
internal sealed class HistoryDeque<T> : IEnumerable<T>
{
    private readonly List<T> _items = [];
    private int _start;

    public int Count => _items.Count - _start;
    public T Oldest => _items[_start];

    public void Push(T item) => _items.Add(item);

    public T Pop()
    {
        var item = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        if (Count == 0)
            Clear();
        return item;
    }

    public bool TryPeek(out T item)
    {
        item = Count > 0 ? _items[^1] : default!;
        return Count > 0;
    }

    public bool TryPop(out T item)
    {
        if (Count > 0)
        {
            item = Pop();
            return true;
        }
        item = default!;
        return false;
    }

    public T RemoveOldest()
    {
        var item = _items[_start];
        _items[_start++] = default!;
        if (_start >= 256 && _start >= Count)
        {
            _items.RemoveRange(0, _start);
            _start = 0;
        }
        if (Count == 0)
            Clear();
        return item;
    }

    public void Clear()
    {
        _items.Clear();
        _start = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = _items.Count - 1; index >= _start; index--)
            yield return _items[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
