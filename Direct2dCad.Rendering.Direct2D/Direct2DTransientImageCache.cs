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
    private readonly Dictionary<EntityId, ID2D1Bitmap> _entityBitmaps = [];
    private readonly Dictionary<byte[], ID2D1Bitmap> _pixelBitmaps = new(ReferenceEqualityComparer.Instance);

    public ID2D1Bitmap? GetOrCreate(ID2D1DeviceContext? deviceContext, CadTransientImage image)
    {
        if (image.SourceEntityId is { } sourceEntityId &&
            _entityBitmaps.TryGetValue(sourceEntityId, out var cachedEntityBitmap))
        {
            return cachedEntityBitmap;
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
                _entityBitmaps[entityId] = bitmap;
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
        var activeEntityImages = scene.Items
            .OfType<CadTransientImage>()
            .Where(image => image.SourceEntityId is not null)
            .Select(image => image.SourceEntityId!.Value)
            .ToHashSet();
        var activeImages = scene.Items
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

            _entityBitmaps[entityId].Dispose();
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
        foreach (var bitmap in _entityBitmaps.Values)
            bitmap.Dispose();

        foreach (var bitmap in _pixelBitmaps.Values)
            bitmap.Dispose();

        _entityBitmaps.Clear();
        _pixelBitmaps.Clear();
    }

    public void Dispose() => Clear();
}
