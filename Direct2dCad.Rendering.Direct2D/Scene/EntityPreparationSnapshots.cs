using System.Collections;

namespace Direct2dCad.Rendering.Direct2D.Scene;

// Copy-on-write pages keep published worker snapshots immutable without copying
// every entity when only a few geometries change.
internal sealed class EntityPreparationSnapshots : IReadOnlyList<EntityPreparationSnapshot>
{
    private const int PageSize = 256;
    private readonly EntityPreparationSnapshot[][] _pages;
    public int Count { get; }

    public EntityPreparationSnapshots(IReadOnlyList<EntityPreparationSnapshot> source)
    {
        Count = source.Count;
        _pages = new EntityPreparationSnapshot[(Count + PageSize - 1) / PageSize][];
        for (var page = 0; page < _pages.Length; page++)
        {
            var entries = new EntityPreparationSnapshot[Math.Min(PageSize, Count - page * PageSize)];
            for (var offset = 0; offset < entries.Length; offset++)
                entries[offset] = source[page * PageSize + offset];
            _pages[page] = entries;
        }
    }

    private EntityPreparationSnapshots(EntityPreparationSnapshot[][] pages, int count)
    {
        _pages = pages;
        Count = count;
    }

    public EntityPreparationSnapshot this[int index] =>
        index >= 0 && index < Count ? _pages[index / PageSize][index % PageSize]
            : throw new ArgumentOutOfRangeException(nameof(index));

    public EntityPreparationSnapshots WithUpdates(IEnumerable<KeyValuePair<int, EntityPreparationSnapshot>> updates)
    {
        var pages = (EntityPreparationSnapshot[][])_pages.Clone();
        var copied = new HashSet<int>();
        foreach (var (index, value) in updates)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(updates));
            var page = index / PageSize;
            if (copied.Add(page))
                pages[page] = (EntityPreparationSnapshot[])pages[page].Clone();
            pages[page][index % PageSize] = value;
        }
        return new EntityPreparationSnapshots(pages, Count);
    }

    public IEnumerator<EntityPreparationSnapshot> GetEnumerator()
    {
        foreach (var page in _pages)
        foreach (var entry in page)
            yield return entry;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
