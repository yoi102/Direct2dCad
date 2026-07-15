using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Transient;
using Vortice.DirectWrite;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DTextFormatResourceCache : IDisposable
{
    private readonly Dictionary<Direct2DTextFormatKey, Entry> _entries = [];
    private readonly HashSet<Direct2DTextFormatKey> _usedThisFrame = [];
    private IDWriteFactory? _writeFactory;
    private int _frameDepth;
    private bool _disposed;

    public void Reset(IDWriteFactory? writeFactory)
    {
        ThrowIfDisposed();
        Clear();
        _writeFactory = writeFactory;
    }

    public void BeginFrame()
    {
        ThrowIfDisposed();
        if (_frameDepth++ == 0)
            _usedThisFrame.Clear();
    }

    public void CompleteFrame()
    {
        ThrowIfDisposed();
        if (_frameDepth == 0 || --_frameDepth > 0)
            return;

        foreach (var key in _entries
                     .Where(pair => pair.Value.ReferenceCount == 0 && !_usedThisFrame.Contains(pair.Key))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RemoveEntry(key);
        }
    }

    public ResourceLease<IDWriteTextFormat>? Acquire(
        CadDocument document,
        CadText text)
    {
        return Acquire(document, text.TextStyleId, text.Height);
    }

    public ResourceLease<IDWriteTextFormat>? Acquire(
        CadDocument document,
        StyleId? textStyleId,
        double height)
    {
        ThrowIfDisposed();
        if (_writeFactory is null)
            return null;

        var key = Direct2DTextServices.CreateTextFormatKey(document, textStyleId, height);
        var entry = GetOrCreate(key);
        entry.ReferenceCount++;
        return new ResourceLease<IDWriteTextFormat>(entry.Format, () => Release(key));
    }

    public IDWriteTextFormat? GetForFrame(
        CadDocument document,
        StyleId? textStyleId,
        double height,
        CadTransientTextFormat? transientFormat = null)
    {
        ThrowIfDisposed();
        if (_writeFactory is null)
            return null;

        var key = transientFormat is null
            ? Direct2DTextServices.CreateTextFormatKey(document, textStyleId, height)
            : Direct2DTextServices.CreateTextFormatKey(transientFormat, height);
        if (_frameDepth > 0)
            _usedThisFrame.Add(key);
        return GetOrCreate(key).Format;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _writeFactory = null;
        _disposed = true;
    }

    private Entry GetOrCreate(Direct2DTextFormatKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
            return entry;

        entry = new Entry(Direct2DTextServices.CreateTextFormat(_writeFactory!, key));
        _entries.Add(key, entry);
        return entry;
    }

    private void Release(Direct2DTextFormatKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;

        if (entry.ReferenceCount > 0)
            entry.ReferenceCount--;
        if (entry.ReferenceCount == 0 && _frameDepth == 0)
            RemoveEntry(key);
    }

    private void RemoveEntry(Direct2DTextFormatKey key)
    {
        if (_entries.Remove(key, out var entry))
            entry.Format.Dispose();
    }

    private void Clear()
    {
        foreach (var entry in _entries.Values)
            entry.Format.Dispose();
        _entries.Clear();
        _usedThisFrame.Clear();
        _frameDepth = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DTextFormatResourceCache));
    }

    private sealed class Entry(IDWriteTextFormat format)
    {
        public IDWriteTextFormat Format { get; } = format;
        public int ReferenceCount { get; set; }
    }
}
