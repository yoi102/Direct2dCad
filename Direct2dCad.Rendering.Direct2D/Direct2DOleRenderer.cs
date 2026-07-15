using System.Numerics;
using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DOleRenderer(Direct2DResourceCache resourceCache) : IDisposable
{
    private const int TilePixelSide = 1024;
    private const int MaxLogicalPixelSide = 1_048_576;
    private readonly Direct2DOleBitmapCache _cache = new();
    private bool _suppressDrawDuringFrame;

    public Direct2DOleDrawCallback? DrawCallback { get; set; }

    public Direct2DOleReleaseCallback? ReleaseCallback { get; set; }

    public void PrepareTiles(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadRenderOptions options)
    {
        var context = resourceCache.DeviceContext;
        if (context is null || DrawCallback is null)
            return;

        var transform = CreateViewportTransform(viewport);
        foreach (var ole in Direct2DEntityVisibility
                     .Enumerate(document, viewport, options, resourceCache)
                     .OfType<CadOleObject>())
        {
            PrepareTiles(context, Direct2DOleRenderKey.ForEntity(ole.Id), ole.Bounds, ole.CopyOleBytes(), viewport, transform);
        }

        if (transientScene is not null)
            PrepareTransientItems(context, document, viewport, transientScene.Items, transform);

        _suppressDrawDuringFrame = true;
    }

    public void DrawEntity(
        ID2D1DeviceContext context,
        CadOleObject ole,
        CadViewport viewport,
        bool allowDraw = true)
    {
        Draw(
            context,
            Direct2DOleRenderKey.ForEntity(ole.Id),
            ole.Bounds,
            ole.CopyOleBytes(),
            ole.Opacity,
            viewport,
            allowDraw);
    }

    public void PrepareEntityTiles(
        ID2D1DeviceContext context,
        CadOleObject ole,
        CadViewport viewport,
        Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ole);
        PrepareTiles(
            context,
            Direct2DOleRenderKey.ForEntity(ole.Id),
            ole.Bounds,
            ole.CopyOleBytes(),
            viewport,
            transform);
    }

    public void PrepareTransientTiles(
        ID2D1DeviceContext context,
        CadTransientOleObject ole,
        CadViewport viewport,
        Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ole);
        PrepareTiles(
            context,
            ole.SourceEntityId is { } sourceId
                ? Direct2DOleRenderKey.ForEntity(sourceId)
                : Direct2DOleRenderKey.ForTransient(ole.RenderId),
            ole.Bounds,
            ole.OleBytes,
            viewport,
            transform);
    }

    public void PrepareTransientSceneTiles(
        ID2D1DeviceContext context,
        CadDocument document,
        CadTransientScene scene,
        CadViewport viewport,
        Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        PrepareTransientItems(context, document, viewport, scene.Items, transform);
    }

    public void DrawTransient(
        ID2D1DeviceContext context,
        CadTransientOleObject ole,
        CadViewport viewport)
    {
        Draw(
            context,
            ole.SourceEntityId is { } sourceId
                ? Direct2DOleRenderKey.ForEntity(sourceId)
                : Direct2DOleRenderKey.ForTransient(ole.RenderId),
            ole.Bounds,
            ole.OleBytes,
            ole.Opacity,
            viewport,
            true);
    }

    public void ReconcileTransient(CadTransientScene scene)
    {
        var activeKeys = EnumerateTransientItems(scene.Items)
            .OfType<CadTransientOleObject>()
            .Where(item => item.SourceEntityId is null)
            .Select(item => Direct2DOleRenderKey.ForTransient(item.RenderId))
            .ToHashSet();
        foreach (var key in _cache.Keys.Where(key => key.IsTransient).ToArray())
        {
            if (activeKeys.Contains(key))
                continue;
            _cache.Remove(key);
            ReleaseCallback?.Invoke(key);
        }
    }

    public void ClearTransient()
    {
        foreach (var key in _cache.Keys.Where(key => key.IsTransient).ToArray())
        {
            _cache.Remove(key);
            ReleaseCallback?.Invoke(key);
        }
    }

    private void PrepareTransientItems(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        IReadOnlyList<CadTransientItem> items,
        Matrix3x2 transform)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case CadTransientGroup group:
                    PrepareTransientItems(
                        context,
                        document,
                        viewport,
                        group.Items,
                        ToMatrix3x2(group.Transform) * transform);
                    break;
                case CadTransientOleObject transient:
                    PrepareTiles(
                        context,
                        transient.SourceEntityId is { } sourceId
                            ? Direct2DOleRenderKey.ForEntity(sourceId)
                            : Direct2DOleRenderKey.ForTransient(transient.RenderId),
                        transient.Bounds,
                        transient.OleBytes,
                        viewport,
                        transform);
                    break;
                case CadTransientEntityReference reference
                    when document.TryGetEntity(reference.EntityId, out var entity) && entity is CadOleObject ole:
                    PrepareTiles(
                        context,
                        Direct2DOleRenderKey.ForEntity(ole.Id),
                        ole.Bounds.Translate(reference.Offset),
                        ole.CopyOleBytes(),
                        viewport,
                        transform);
                    break;
            }
        }
    }

    private static IEnumerable<CadTransientItem> EnumerateTransientItems(IEnumerable<CadTransientItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is not CadTransientGroup group)
                continue;

            foreach (var child in EnumerateTransientItems(group.Items))
                yield return child;
        }
    }

    private static Matrix3x2 ToMatrix3x2(CadMatrixD transform) => new(
        (float)transform.M11,
        (float)transform.M12,
        (float)transform.M21,
        (float)transform.M22,
        (float)transform.OffsetX,
        (float)transform.OffsetY);

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & CadEntityChangeKind.Deleted) != 0)
            {
                RemoveEntity(change.EntityId);
                continue;
            }

            if ((change.Kind & CadEntityChangeKind.Appearance) != 0 &&
                document.TryGetEntity(change.EntityId, out var entity) &&
                entity is CadOleObject)
            {
                RemoveEntity(change.EntityId);
            }
        }
    }

    public void RemoveEntity(EntityId entityId) => _cache.Remove(Direct2DOleRenderKey.ForEntity(entityId));

    public void CompleteFrame()
    {
        _cache.CompleteFrame();
        _suppressDrawDuringFrame = false;
    }

    public void Clear()
    {
        _cache.Clear();
        _suppressDrawDuringFrame = false;
    }

    public void Dispose() => Clear();

    private void PrepareTiles(
        ID2D1DeviceContext context,
        Direct2DOleRenderKey key,
        CadRectD bounds,
        byte[] bytes,
        CadViewport viewport,
        Matrix3x2 transform)
    {
        if (!TryResolveDestinations(bounds, viewport, transform, out var full, out var visible))
            return;

        var size = ResolveRenderSize(full.Right - full.Left, full.Bottom - full.Top);
        if (_cache.TryGetValue(key, out var active) && active.CanReuseFor(size.Width, size.Height))
        {
            var visibleTiles = ResolveVisibleTiles(full, visible, active);
            if (TryPopulateTiles(context, key, bytes, active, visibleTiles))
                active.RetainTiles(visibleTiles);
            return;
        }

        var replacement = new Direct2DOleBitmapCache.Entry(size.Width, size.Height);
        var replacementTiles = ResolveVisibleTiles(full, visible, replacement);
        if (!TryPopulateTiles(context, key, bytes, replacement, replacementTiles))
        {
            replacement.Dispose();
            return;
        }

        _cache.Set(key, replacement);
        active?.Dispose();
    }

    private void Draw(
        ID2D1DeviceContext context,
        Direct2DOleRenderKey key,
        CadRectD bounds,
        byte[] bytes,
        double opacity,
        CadViewport viewport,
        bool allowDraw)
    {
        allowDraw &= !_suppressDrawDuringFrame;
        if (!TryResolveDestinations(bounds, viewport, context.Transform, out var full, out var visible))
            return;

        var size = ResolveRenderSize(full.Right - full.Left, full.Bottom - full.Top);
        if (!_cache.TryGetValue(key, out var active))
        {
            if (!allowDraw)
                return;
            var initial = new Direct2DOleBitmapCache.Entry(size.Width, size.Height);
            var tiles = ResolveVisibleTiles(full, visible, initial);
            if (!TryPopulateTiles(context, key, bytes, initial, tiles))
            {
                initial.Dispose();
                return;
            }

            _cache.Set(key, initial);
            DrawTiles(context, full, initial, tiles, opacity);
            return;
        }

        if (active.CanReuseFor(size.Width, size.Height))
        {
            var tiles = ResolveVisibleTiles(full, visible, active);
            if (tiles.All(active.Tiles.ContainsKey))
            {
                DrawTiles(context, full, active, tiles, opacity);
                active.RetainTiles(tiles);
                return;
            }

            var drewFallback = DrawTiles(context, full, active, tiles, opacity);
            if (!allowDraw)
                return;
            if (drewFallback)
                context.Flush(out _, out _);
            _ = TryPopulateTiles(context, key, bytes, active, tiles);
            DrawTiles(context, full, active, tiles, opacity);
            active.RetainTiles(tiles);
            return;
        }

        var fallbackTiles = ResolveVisibleTiles(full, visible, active);
        if (DrawTiles(context, full, active, fallbackTiles, opacity))
            context.Flush(out _, out _);
        if (!allowDraw)
            return;

        var replacement = new Direct2DOleBitmapCache.Entry(size.Width, size.Height);
        var replacementTiles = ResolveVisibleTiles(full, visible, replacement);
        if (!TryPopulateTiles(context, key, bytes, replacement, replacementTiles))
        {
            replacement.Dispose();
            return;
        }

        _cache.Set(key, replacement);
        DrawTiles(context, full, replacement, replacementTiles, opacity);
        _cache.Retire(active);
    }

    private bool TryPopulateTiles(
        ID2D1DeviceContext context,
        Direct2DOleRenderKey key,
        byte[] bytes,
        Direct2DOleBitmapCache.Entry entry,
        IReadOnlySet<Direct2DOleBitmapCache.TileKey> visibleTiles)
    {
        var complete = true;
        foreach (var tileKey in visibleTiles)
        {
            if (entry.Tiles.ContainsKey(tileKey))
                continue;
            var bitmap = CreateTileBitmap(context, key, bytes, entry, tileKey);
            if (bitmap is null)
            {
                complete = false;
                continue;
            }
            entry.Tiles[tileKey] = bitmap;
        }
        return complete;
    }

    private ID2D1Bitmap? CreateTileBitmap(
        ID2D1DeviceContext context,
        Direct2DOleRenderKey key,
        byte[] bytes,
        Direct2DOleBitmapCache.Entry entry,
        Direct2DOleBitmapCache.TileKey tileKey)
    {
        var x = tileKey.Column * TilePixelSide;
        var y = tileKey.Row * TilePixelSide;
        var width = Math.Min(TilePixelSide, entry.PixelWidth - x);
        var height = Math.Min(TilePixelSide, entry.PixelHeight - y);
        Direct2DOleDrawData? data = null;
        if (DrawCallback is not null)
        {
            try
            {
                data = DrawCallback(new Direct2DOleDrawRequest(
                    key,
                    bytes,
                    entry.PixelWidth,
                    entry.PixelHeight,
                    x,
                    y,
                    width,
                    height));
            }
            catch
            {
                data = null;
            }
        }

        return IsValidDrawData(data) && data!.PixelWidth == width && data.PixelHeight == height
            ? CreateBitmap(context, data)
            : null;
    }

    private static bool DrawTiles(
        ID2D1DeviceContext context,
        RawRectF full,
        Direct2DOleBitmapCache.Entry entry,
        IReadOnlySet<Direct2DOleBitmapCache.TileKey> tiles,
        double opacity)
    {
        var drew = false;
        foreach (var key in tiles)
        {
            if (!entry.Tiles.TryGetValue(key, out var bitmap))
                continue;
            DrawBitmapInDeviceSpace(context, bitmap, CreateTileDestination(full, entry, key), opacity);
            drew = true;
        }
        return drew;
    }

    private static bool TryResolveDestinations(
        CadRectD bounds,
        CadViewport viewport,
        Matrix3x2 transform,
        out RawRectF full,
        out RawRectF visible)
    {
        full = default;
        visible = default;
        if (bounds.IsEmpty)
            return false;
        full = TransformWorldBoundsToDeviceRect(bounds, transform);
        if (full.Right <= full.Left || full.Bottom <= full.Top)
            return false;
        visible = IntersectRects(full, new RawRectF(0, 0, (float)viewport.ViewWidth, (float)viewport.ViewHeight));
        return visible.Right > visible.Left && visible.Bottom > visible.Top;
    }

    private static void DrawBitmapInDeviceSpace(
        ID2D1DeviceContext context,
        ID2D1Bitmap bitmap,
        RawRectF destination,
        double opacity)
    {
        var transform = context.Transform;
        context.Transform = Matrix3x2.Identity;
        try
        {
            context.DrawBitmap(bitmap, destination, ToOpacity(opacity), InterpolationMode.Linear, null, null);
        }
        finally
        {
            context.Transform = transform;
        }
    }

    private static RawRectF TransformWorldBoundsToDeviceRect(CadRectD bounds, Matrix3x2 transform)
    {
        var points = new[]
        {
            Vector2.Transform(new Vector2((float)bounds.MinX, (float)bounds.MaxY), transform),
            Vector2.Transform(new Vector2((float)bounds.MaxX, (float)bounds.MaxY), transform),
            Vector2.Transform(new Vector2((float)bounds.MinX, (float)bounds.MinY), transform),
            Vector2.Transform(new Vector2((float)bounds.MaxX, (float)bounds.MinY), transform)
        };
        return new RawRectF(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static (int Width, int Height) ResolveRenderSize(float width, float height)
    {
        var pixelWidth = Math.Max(1, (int)Math.Min(Math.Ceiling(Math.Max(width, 1.0f)), int.MaxValue));
        var pixelHeight = Math.Max(1, (int)Math.Min(Math.Ceiling(Math.Max(height, 1.0f)), int.MaxValue));
        var maxSide = Math.Max(pixelWidth, pixelHeight);
        if (maxSide <= MaxLogicalPixelSide)
            return (pixelWidth, pixelHeight);
        var scale = MaxLogicalPixelSide / (double)maxSide;
        return (Math.Max(1, (int)Math.Round(pixelWidth * scale)), Math.Max(1, (int)Math.Round(pixelHeight * scale)));
    }

    private static HashSet<Direct2DOleBitmapCache.TileKey> ResolveVisibleTiles(
        RawRectF full,
        RawRectF visible,
        Direct2DOleBitmapCache.Entry entry)
    {
        var width = full.Right - full.Left;
        var height = full.Bottom - full.Top;
        var minX = Math.Clamp((int)Math.Floor((visible.Left - full.Left) / width * entry.PixelWidth), 0, entry.PixelWidth - 1);
        var minY = Math.Clamp((int)Math.Floor((visible.Top - full.Top) / height * entry.PixelHeight), 0, entry.PixelHeight - 1);
        var maxX = Math.Clamp((int)Math.Ceiling((visible.Right - full.Left) / width * entry.PixelWidth), minX + 1, entry.PixelWidth);
        var maxY = Math.Clamp((int)Math.Ceiling((visible.Bottom - full.Top) / height * entry.PixelHeight), minY + 1, entry.PixelHeight);
        var result = new HashSet<Direct2DOleBitmapCache.TileKey>();
        for (var row = minY / TilePixelSide; row <= (maxY - 1) / TilePixelSide; row++)
        for (var column = minX / TilePixelSide; column <= (maxX - 1) / TilePixelSide; column++)
            result.Add(new Direct2DOleBitmapCache.TileKey(column, row));
        return result;
    }

    private static RawRectF CreateTileDestination(
        RawRectF full,
        Direct2DOleBitmapCache.Entry entry,
        Direct2DOleBitmapCache.TileKey key)
    {
        var x = key.Column * TilePixelSide;
        var y = key.Row * TilePixelSide;
        var width = Math.Min(TilePixelSide, entry.PixelWidth - x);
        var height = Math.Min(TilePixelSide, entry.PixelHeight - y);
        var destinationWidth = full.Right - full.Left;
        var destinationHeight = full.Bottom - full.Top;
        return new RawRectF(
            full.Left + x / (float)entry.PixelWidth * destinationWidth,
            full.Top + y / (float)entry.PixelHeight * destinationHeight,
            full.Left + (x + width) / (float)entry.PixelWidth * destinationWidth,
            full.Top + (y + height) / (float)entry.PixelHeight * destinationHeight);
    }

    private static RawRectF IntersectRects(RawRectF left, RawRectF right)
    {
        return new RawRectF(
            MathF.Max(left.Left, right.Left),
            MathF.Max(left.Top, right.Top),
            MathF.Min(left.Right, right.Right),
            MathF.Min(left.Bottom, right.Bottom));
    }

    private static bool IsValidDrawData(Direct2DOleDrawData? data)
    {
        return data is not null && data.PixelWidth > 0 && data.PixelHeight > 0 &&
               data.Stride >= data.PixelWidth * 4 && data.Pixels.Length >= data.Stride * data.PixelHeight;
    }

    private static ID2D1Bitmap CreateBitmap(ID2D1DeviceContext context, Direct2DOleDrawData data)
    {
        var handle = GCHandle.Alloc(data.Pixels, GCHandleType.Pinned);
        try
        {
            return context.CreateBitmap(
                new SizeI(data.PixelWidth, data.PixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)data.Stride,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(DXGIFormat.B8G8R8A8_UNorm, AlphaMode.Ignore),
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

    private static Matrix3x2 CreateViewportTransform(CadViewport viewport)
    {
        return Matrix3x2.CreateScale((float)viewport.Zoom, (float)-viewport.Zoom) *
               Matrix3x2.CreateTranslation((float)viewport.Offset.X, (float)viewport.Offset.Y);
    }

    private static float ToOpacity(double opacity)
    {
        return double.IsFinite(opacity) ? (float)Math.Clamp(opacity, 0.0, 1.0) : 1.0f;
    }
}
