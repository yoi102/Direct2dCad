using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Rendering.Transient;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DTransientImageCache : IDisposable
{
    private readonly Dictionary<EntityId, EntityBitmapEntry> _entityBitmaps = [];
    private readonly Dictionary<byte[], ID2D1Bitmap> _pixelBitmaps = new(ReferenceEqualityComparer.Instance);

    public ID2D1Bitmap? GetOrCreate(ID2D1DeviceContext? deviceContext, CadTransientImage image)
    {
        if (image.SourceEntityId is { } sourceEntityId &&
            _entityBitmaps.TryGetValue(sourceEntityId, out var cachedEntityBitmap) &&
            ReferenceEquals(cachedEntityBitmap.PixelSource, image.Pixels))
        {
            return cachedEntityBitmap.Bitmap;
        }

        if (image.SourceEntityId is { } changedSourceEntityId &&
            _entityBitmaps.Remove(changedSourceEntityId, out var staleEntityBitmap))
        {
            staleEntityBitmap.Bitmap.Dispose();
        }

        if (_pixelBitmaps.TryGetValue(image.Pixels, out var cached))
            return cached;

        if (deviceContext is null ||
            image.PixelWidth <= 0 ||
            image.PixelHeight <= 0 ||
            image.Stride < image.PixelWidth * 4 ||
            image.Pixels.Length < image.Stride * image.PixelHeight)
        {
            return null;
        }

        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            var bitmap = deviceContext.CreateBitmap(
                new SizeI(image.PixelWidth, image.PixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)image.Stride,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(DXGIFormat.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.None
                });

            if (image.SourceEntityId is { } entityId)
                _entityBitmaps[entityId] = new EntityBitmapEntry(image.Pixels, bitmap);
            else
                _pixelBitmaps[image.Pixels] = bitmap;

            return bitmap;
        }
        finally
        {
            handle.Free();
        }
    }

    public void Reconcile(CadTransientScene scene)
    {
        var transientItems = EnumerateItems(scene.Items).ToArray();
        var activeEntityImages = transientItems
            .OfType<CadTransientImage>()
            .Where(image => image.SourceEntityId is not null)
            .Select(image => image.SourceEntityId!.Value)
            .ToHashSet();
        var activeImages = transientItems
            .OfType<CadTransientImage>()
            .Where(image => image.SourceEntityId is null)
            .Select(image => image.Pixels)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        if (activeEntityImages.Count == 0 && activeImages.Count == 0)
        {
            Clear();
            return;
        }

        foreach (var entityId in _entityBitmaps.Keys.ToArray())
        {
            if (activeEntityImages.Contains(entityId))
                continue;

            _entityBitmaps[entityId].Bitmap.Dispose();
            _entityBitmaps.Remove(entityId);
        }

        foreach (var pixels in _pixelBitmaps.Keys.ToArray())
        {
            if (activeImages.Contains(pixels))
                continue;

            _pixelBitmaps[pixels].Dispose();
            _pixelBitmaps.Remove(pixels);
        }
    }

    public void Clear()
    {
        foreach (var entry in _entityBitmaps.Values)
            entry.Bitmap.Dispose();

        foreach (var bitmap in _pixelBitmaps.Values)
            bitmap.Dispose();

        _entityBitmaps.Clear();
        _pixelBitmaps.Clear();
    }

    public void Dispose() => Clear();

    private static IEnumerable<CadTransientItem> EnumerateItems(IEnumerable<CadTransientItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is not CadTransientGroup group)
                continue;

            foreach (var child in EnumerateItems(group.Items))
                yield return child;
        }
    }

    private sealed record EntityBitmapEntry(byte[] PixelSource, ID2D1Bitmap Bitmap);
}
