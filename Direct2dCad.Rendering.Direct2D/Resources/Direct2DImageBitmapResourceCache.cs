using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DImageBitmapResourceCache : IDisposable
{
    private readonly Dictionary<ImageBitmapKey, Entry> _entries = [];
    private readonly Action<ImageBitmapKey> _release;
    private ID2D1DeviceContext? _deviceContext;
    private long _estimatedBytes;
    private bool _disposed;

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

    public Direct2DImageBitmapResourceCache(ID2D1DeviceContext? deviceContext = null)
    {
        _deviceContext = deviceContext;
        _release = Release;
    }

    public void Reset(ID2D1DeviceContext? deviceContext)
    {
        ThrowIfDisposed();
        Clear();
        _deviceContext = deviceContext;
    }

    public KeyedResourceLease<ID2D1Bitmap, ImageBitmapKey>? Acquire(CadImage image)
    {
        ThrowIfDisposed();
        if (_deviceContext is null)
            return null;

        var key = new ImageBitmapKey(image.Id, image.Pixels);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(
                CreateBitmap(_deviceContext, image),
                checked((long)image.Stride * image.PixelHeight));
            _entries.Add(key, entry);
            _estimatedBytes += entry.EstimatedBytes;
        }

        entry.ReferenceCount++;
        return new KeyedResourceLease<ID2D1Bitmap, ImageBitmapKey>(
            entry.Bitmap,
            key,
            _release);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        DisposeEntries();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeEntries();
        _deviceContext = null;
        _disposed = true;
    }

    private static ID2D1Bitmap CreateBitmap(ID2D1DeviceContext context, CadImage image)
    {
        var pixels = image.CopyPixels();
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return context.CreateBitmap(
                new SizeI(image.PixelWidth, image.PixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)image.Stride,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DXGIFormat.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.None
                });
        }
        finally
        {
            handle.Free();
        }
    }

    private void Release(ImageBitmapKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;

        if (entry.ReferenceCount > 0)
            entry.ReferenceCount--;
        if (entry.ReferenceCount > 0)
            return;

        _entries.Remove(key);
        _estimatedBytes -= entry.EstimatedBytes;
        entry.Bitmap.Dispose();
    }

    private void DisposeEntries()
    {
        foreach (var entry in _entries.Values)
            entry.Bitmap.Dispose();
        _entries.Clear();
        _estimatedBytes = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DImageBitmapResourceCache));
    }

    internal readonly record struct ImageBitmapKey(EntityId EntityId, IReadOnlyList<byte> PixelSource);

    private sealed class Entry(ID2D1Bitmap bitmap, long estimatedBytes)
    {
        public ID2D1Bitmap Bitmap { get; } = bitmap;
        public long EstimatedBytes { get; } = estimatedBytes;
        public int ReferenceCount { get; set; }
    }
}
