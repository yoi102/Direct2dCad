using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Rendering.Transient;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Transient;

internal sealed class Direct2DTransientImageCache : IDisposable
{
    private readonly Dictionary<EntityId, EntityBitmapEntry> _entityBitmaps = [];
    private readonly Dictionary<byte[], ID2D1Bitmap> _pixelBitmaps = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<EntityId> _activeEntityImages = [];
    private readonly HashSet<byte[]> _activePixelImages = new(ReferenceEqualityComparer.Instance);
    private readonly List<EntityId> _staleEntityIds = [];
    private readonly List<byte[]> _stalePixelSources = [];
    private CadTransientScene? _reconciledScene;
    private long _reconciledVersion = -1;

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
        if (ReferenceEquals(_reconciledScene, scene) &&
            _reconciledVersion == scene.Version)
        {
            return;
        }

        _reconciledScene = scene;
        _reconciledVersion = scene.Version;
        if (_entityBitmaps.Count == 0 && _pixelBitmaps.Count == 0)
            return;

        _activeEntityImages.Clear();
        _activePixelImages.Clear();
        CollectActiveImages(scene.Items);

        if (_activeEntityImages.Count == 0 && _activePixelImages.Count == 0)
        {
            ClearBitmaps();
            return;
        }

        _staleEntityIds.Clear();
        foreach (var entityId in _entityBitmaps.Keys)
        {
            if (!_activeEntityImages.Contains(entityId))
                _staleEntityIds.Add(entityId);
        }
        foreach (var entityId in _staleEntityIds)
        {
            _entityBitmaps[entityId].Bitmap.Dispose();
            _entityBitmaps.Remove(entityId);
        }

        _stalePixelSources.Clear();
        foreach (var pixels in _pixelBitmaps.Keys)
        {
            if (!_activePixelImages.Contains(pixels))
                _stalePixelSources.Add(pixels);
        }
        foreach (var pixels in _stalePixelSources)
        {
            _pixelBitmaps[pixels].Dispose();
            _pixelBitmaps.Remove(pixels);
        }

        _activeEntityImages.Clear();
        _activePixelImages.Clear();
    }

    public void Clear()
    {
        ClearBitmaps();
        _reconciledScene = null;
        _reconciledVersion = -1;
    }

    public void Dispose() => Clear();

    private void ClearBitmaps()
    {
        foreach (var entry in _entityBitmaps.Values)
            entry.Bitmap.Dispose();

        foreach (var bitmap in _pixelBitmaps.Values)
            bitmap.Dispose();

        _entityBitmaps.Clear();
        _pixelBitmaps.Clear();
        _activeEntityImages.Clear();
        _activePixelImages.Clear();
        _staleEntityIds.Clear();
        _stalePixelSources.Clear();
    }

    private void CollectActiveImages(IReadOnlyList<CadTransientItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case CadTransientImage { SourceEntityId: { } entityId }:
                    _activeEntityImages.Add(entityId);
                    break;
                case CadTransientImage image:
                    _activePixelImages.Add(image.Pixels);
                    break;
                case CadTransientGroup group:
                    CollectActiveImages(group.Items);
                    break;
            }
        }
    }

    private sealed record EntityBitmapEntry(byte[] PixelSource, ID2D1Bitmap Bitmap);
}
