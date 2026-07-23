using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Ole;

internal sealed class Direct2DOleBitmapCache : IDisposable
{
    private const double MaxDownscaleReuse = 2.0;
    internal const long CacheBudgetBytes = 128L * 1024 * 1024;
    private readonly Dictionary<Direct2DOleRenderKey, Entry> _entries = [];
    private readonly List<Entry> _retiredEntries = [];
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private long _usageStamp;
    private long _estimatedBytes;

    public Direct2DOleBitmapCache(Direct2DRenderStatisticsCollector statistics)
    {
        _statistics = statistics;
    }

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

    public IEnumerable<Direct2DOleRenderKey> Keys => _entries.Keys;

    public bool TryGetValue(Direct2DOleRenderKey key, out Entry entry)
    {
        if (_entries.TryGetValue(key, out var found))
        {
            found.LastUsed = ++_usageStamp;
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    public void Set(Direct2DOleRenderKey key, Entry entry)
    {
        entry.LastUsed = ++_usageStamp;
        if (_entries.TryGetValue(key, out var previous))
        {
            previous.DetachEstimatedBytesChanged();
            _estimatedBytes -= previous.EstimatedBytes;
        }
        entry.AttachEstimatedBytesChanged(AdjustEstimatedBytes);
        _entries[key] = entry;
        _estimatedBytes += entry.EstimatedBytes;
        TrimToBudget(key);
    }

    public void Retire(Entry entry)
    {
        entry.AttachEstimatedBytesChanged(AdjustEstimatedBytes);
        _retiredEntries.Add(entry);
        _estimatedBytes += entry.EstimatedBytes;
    }

    public void TrimToBudget(Direct2DOleRenderKey protectedKey)
    {
        while (EstimatedBytes > CacheBudgetBytes)
        {
            var candidate = _entries
                .Where(pair => !pair.Key.Equals(protectedKey))
                .OrderBy(static pair => pair.Value.LastUsed)
                .FirstOrDefault();
            if (candidate.Value is null)
                return;

            _entries.Remove(candidate.Key);
            _estimatedBytes -= candidate.Value.EstimatedBytes;
            candidate.Value.DetachEstimatedBytesChanged();
            candidate.Value.Dispose();
            _statistics.RecordGpuCacheEviction();
        }
    }

    public bool Remove(Direct2DOleRenderKey key)
    {
        if (!_entries.Remove(key, out var entry))
            return false;

        _estimatedBytes -= entry.EstimatedBytes;
        entry.DetachEstimatedBytesChanged();
        entry.Dispose();
        return true;
    }

    public void CompleteFrame()
    {
        foreach (var entry in _retiredEntries)
        {
            _estimatedBytes -= entry.EstimatedBytes;
            entry.DetachEstimatedBytesChanged();
            entry.Dispose();
        }

        _retiredEntries.Clear();
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            entry.DetachEstimatedBytesChanged();
            entry.Dispose();
        }

        _entries.Clear();
        CompleteFrame();
        _estimatedBytes = 0;
    }

    public void Dispose() => Clear();

    private void AdjustEstimatedBytes(long delta)
    {
        _estimatedBytes = Math.Max(0, _estimatedBytes + delta);
    }

    internal readonly record struct TileKey(int Column, int Row);

    internal sealed class Entry(int pixelWidth, int pixelHeight) : IDisposable
    {
        private readonly List<TileKey> _staleTileKeys = [];
        private readonly Dictionary<TileKey, long> _tileBytes = [];
        private Action<long>? _estimatedBytesChanged;

        public int PixelWidth { get; } = pixelWidth;

        public int PixelHeight { get; } = pixelHeight;

        public Dictionary<TileKey, ID2D1Bitmap> Tiles { get; } = [];
        public long EstimatedBytes { get; private set; }
        public long LastUsed { get; set; }

        public void SetTile(TileKey key, ID2D1Bitmap bitmap, long estimatedBytes)
        {
            if (Tiles.Remove(key, out var previous))
            {
                previous.Dispose();
                UpdateEstimatedBytes(EstimatedBytes - _tileBytes[key]);
            }

            Tiles[key] = bitmap;
            _tileBytes[key] = Math.Max(0, estimatedBytes);
            UpdateEstimatedBytes(EstimatedBytes + Math.Max(0, estimatedBytes));
        }

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
            _staleTileKeys.Clear();
            foreach (var key in Tiles.Keys)
            {
                if (!retainedTiles.Contains(key))
                    _staleTileKeys.Add(key);
            }

            foreach (var key in _staleTileKeys)
            {
                Tiles[key].Dispose();
                Tiles.Remove(key);
                UpdateEstimatedBytes(EstimatedBytes - _tileBytes[key]);
                _tileBytes.Remove(key);
            }
        }

        internal void AttachEstimatedBytesChanged(Action<long> callback)
        {
            _estimatedBytesChanged = callback;
        }

        internal void DetachEstimatedBytesChanged()
        {
            _estimatedBytesChanged = null;
        }

        public void Dispose()
        {
            foreach (var bitmap in Tiles.Values)
                bitmap.Dispose();

            Tiles.Clear();
            _tileBytes.Clear();
            UpdateEstimatedBytes(0);
        }

        private void UpdateEstimatedBytes(long value)
        {
            var normalized = Math.Max(0, value);
            var delta = normalized - EstimatedBytes;
            EstimatedBytes = normalized;
            _estimatedBytesChanged?.Invoke(delta);
        }
    }
}
