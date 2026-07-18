using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Ole;

internal sealed class Direct2DOleBitmapCache : IDisposable
{
    private const double MaxDownscaleReuse = 2.0;
    private readonly Dictionary<Direct2DOleRenderKey, Entry> _entries = [];
    private readonly List<Entry> _retiredEntries = [];

    public IEnumerable<Direct2DOleRenderKey> Keys => _entries.Keys;

    public bool TryGetValue(Direct2DOleRenderKey key, out Entry entry)
    {
        if (_entries.TryGetValue(key, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    public void Set(Direct2DOleRenderKey key, Entry entry)
    {
        _entries[key] = entry;
    }

    public void Retire(Entry entry)
    {
        _retiredEntries.Add(entry);
    }

    public bool Remove(Direct2DOleRenderKey key)
    {
        if (!_entries.Remove(key, out var entry))
            return false;

        entry.Dispose();
        return true;
    }

    public void CompleteFrame()
    {
        foreach (var entry in _retiredEntries)
            entry.Dispose();

        _retiredEntries.Clear();
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
            entry.Dispose();

        _entries.Clear();
        CompleteFrame();
    }

    public void Dispose() => Clear();

    internal readonly record struct TileKey(int Column, int Row);

    internal sealed class Entry(int pixelWidth, int pixelHeight) : IDisposable
    {
        public int PixelWidth { get; } = pixelWidth;

        public int PixelHeight { get; } = pixelHeight;

        public Dictionary<TileKey, ID2D1Bitmap> Tiles { get; } = [];

        public bool CanReuseFor(int requestedPixelWidth, int requestedPixelHeight)
        {
            if (PixelWidth < requestedPixelWidth || PixelHeight < requestedPixelHeight)
                return false;

            if (PixelWidth > requestedPixelWidth * MaxDownscaleReuse ||
                PixelHeight > requestedPixelHeight * MaxDownscaleReuse)
            {
                return false;
            }

            var cachedAspect = PixelWidth / (double)PixelHeight;
            var requestedAspect = requestedPixelWidth / (double)requestedPixelHeight;
            return Math.Abs(cachedAspect - requestedAspect) <= Math.Max(cachedAspect, requestedAspect) * 1e-4;
        }

        public void RetainTiles(IReadOnlySet<TileKey> retainedTiles)
        {
            foreach (var key in Tiles.Keys.ToArray())
            {
                if (retainedTiles.Contains(key))
                    continue;

                Tiles[key].Dispose();
                Tiles.Remove(key);
            }
        }

        public void Dispose()
        {
            foreach (var bitmap in Tiles.Values)
                bitmap.Dispose();

            Tiles.Clear();
        }
    }
}
