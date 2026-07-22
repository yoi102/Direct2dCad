using System.Numerics;
using System.Runtime.InteropServices;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Ole;

internal sealed class Direct2DOleRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DEntityOrderCache entityOrderCache,
    Direct2DStyleResourceCache styleResources,
    Direct2DRenderStatisticsCollector statistics) : IDisposable
{
    private const int TilePixelSide = 1024;
    private const int MaxLogicalPixelSide = 1_048_576;
    private readonly Direct2DOleBitmapCache _cache = new();
    private readonly Dictionary<EntityId, byte[]> _entityOleBytes = [];
    private readonly HashSet<Direct2DOleRenderKey> _activeTransientKeys = [];
    private readonly HashSet<Direct2DOleRenderKey> _cachedTransientKeys = [];
    private readonly List<Direct2DOleRenderKey> _staleTransientKeys = [];
    private readonly HashSet<Direct2DOleBitmapCache.TileKey> _visibleTileKeys = [];
    private CadTransientScene? _reconciledTransientScene;
    private long _reconciledTransientVersion = -1;
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
        var orderedOleEntities = entityOrderCache.GetOrderedOleEntities(
            document,
            options.ActiveOwnerBlockId);
        if (orderedOleEntities.Count > 0)
        {
            foreach (var ole in Direct2DEntityVisibility
                         .Enumerate(
                             document,
                             viewport,
                             options,
                             resourceCache,
                             orderedOleEntities,
                             entityOrderCache)
                         .Cast<CadOleObject>())
            {
                if (Direct2DEntityLevelOfDetail.ResolveOle(ole.Bounds, transform, options) !=
                    Direct2DEntityRenderDetail.Full)
                {
                    continue;
                }

                PrepareTiles(
                    context,
                    Direct2DOleRenderKey.ForEntity(ole.Id),
                    ole.Bounds,
                    GetEntityBytes(ole),
                    viewport,
                    transform);
            }
        }

        if (transientScene is not null)
            PrepareTransientItems(
                context,
                document,
                viewport,
                transientScene.Items,
                transform,
                options);

        _suppressDrawDuringFrame = true;
    }

    public void DrawEntity(
        ID2D1DeviceContext context,
        CadDocument document,
        CadOleObject ole,
        CadViewport viewport,
        CadRenderOptions options,
        CadColor? proxyColorOverride = null,
        bool allowDraw = true)
    {
        var detail = Direct2DEntityLevelOfDetail.Resolve(
            ole,
            resources: null,
            context.Transform,
            options);
        if (detail == Direct2DEntityRenderDetail.Skip)
            return;
        if (detail == Direct2DEntityRenderDetail.Simplified)
        {
            var color = proxyColorOverride ?? ResolveLayerColor(document, ole.LayerId);
            var brush = styleResources.GetBrush(context, color);
            Direct2DEntityRenderer.DrawRectangularProxy(
                context,
                ole.Bounds,
                brush,
                options.TransformScaleMultiplier);
            return;
        }

        Draw(
            context,
            Direct2DOleRenderKey.ForEntity(ole.Id),
            ole.Bounds,
            GetEntityBytes(ole),
            ole.Opacity,
            viewport,
            allowDraw);
    }

    public void PrepareEntityTiles(
        ID2D1DeviceContext context,
        CadOleObject ole,
        CadViewport viewport,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ole);
        if (Direct2DEntityLevelOfDetail.ResolveOle(ole.Bounds, transform, options) !=
            Direct2DEntityRenderDetail.Full)
        {
            return;
        }

        PrepareTiles(
            context,
            Direct2DOleRenderKey.ForEntity(ole.Id),
            ole.Bounds,
            GetEntityBytes(ole),
            viewport,
            transform);
    }

    public void PrepareTransientTiles(
        ID2D1DeviceContext context,
        CadTransientOleObject ole,
        CadViewport viewport,
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ole);
        if (Direct2DEntityLevelOfDetail.ResolveOle(ole.Bounds, transform, options) !=
            Direct2DEntityRenderDetail.Full)
        {
            return;
        }

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
        Matrix3x2 transform,
        CadRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        PrepareTransientItems(context, document, viewport, scene.Items, transform, options);
    }

    public void DrawTransient(
        ID2D1DeviceContext context,
        CadTransientOleObject ole,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var detail = Direct2DEntityLevelOfDetail.ResolveOle(
            ole.Bounds,
            context.Transform,
            options);
        if (detail == Direct2DEntityRenderDetail.Skip)
            return;
        if (detail == Direct2DEntityRenderDetail.Simplified)
        {
            var brush = styleResources.GetBrush(context, ole.Style.StrokeColor);
            Direct2DEntityRenderer.DrawRectangularProxy(
                context,
                ole.Bounds,
                brush,
                options.TransformScaleMultiplier);
            return;
        }

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
        if (ReferenceEquals(_reconciledTransientScene, scene) &&
            _reconciledTransientVersion == scene.Version)
        {
            return;
        }

        _reconciledTransientScene = scene;
        _reconciledTransientVersion = scene.Version;
        _activeTransientKeys.Clear();
        CollectTransientKeys(scene.Items);
        _staleTransientKeys.Clear();
        foreach (var key in _cachedTransientKeys)
        {
            if (!_activeTransientKeys.Contains(key))
                _staleTransientKeys.Add(key);
        }

        foreach (var key in _staleTransientKeys)
        {
            _cache.Remove(key);
            _cachedTransientKeys.Remove(key);
            ReleaseCallback?.Invoke(key);
        }

        _activeTransientKeys.Clear();
    }

    public void ClearTransient()
    {
        _reconciledTransientScene = null;
        _reconciledTransientVersion = -1;
        if (_cachedTransientKeys.Count == 0)
            return;

        _staleTransientKeys.Clear();
        _staleTransientKeys.AddRange(_cachedTransientKeys);

        foreach (var key in _staleTransientKeys)
        {
            _cache.Remove(key);
            ReleaseCallback?.Invoke(key);
        }
        _cachedTransientKeys.Clear();
    }

    private void PrepareTransientItems(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        IReadOnlyList<CadTransientItem> items,
        Matrix3x2 transform,
        CadRenderOptions options)
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
                        ToMatrix3x2(group.Transform) * transform,
                        options);
                    break;
                case CadTransientOleObject transient:
                    if (Direct2DEntityLevelOfDetail.ResolveOle(
                            transient.Bounds,
                            transform,
                            options) !=
                        Direct2DEntityRenderDetail.Full)
                    {
                        break;
                    }

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
                    var translatedBounds = ole.Bounds.Translate(reference.Offset);
                    if (Direct2DEntityLevelOfDetail.ResolveOle(
                            translatedBounds,
                            transform,
                            options) !=
                        Direct2DEntityRenderDetail.Full)
                    {
                        break;
                    }

                    PrepareTiles(
                        context,
                        Direct2DOleRenderKey.ForEntity(ole.Id),
                        translatedBounds,
                        GetEntityBytes(ole),
                        viewport,
                        transform);
                    break;
            }
        }
    }

    private void CollectTransientKeys(IReadOnlyList<CadTransientItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case CadTransientOleObject { SourceEntityId: null } ole:
                    _activeTransientKeys.Add(
                        Direct2DOleRenderKey.ForTransient(ole.RenderId));
                    break;
                case CadTransientGroup group:
                    CollectTransientKeys(group.Items);
                    break;
            }
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

            if ((change.Kind &
                 (CadEntityChangeKind.Appearance | CadEntityChangeKind.EmbeddedData)) != 0 &&
                document.TryGetEntity(change.EntityId, out var entity) &&
                entity is CadOleObject)
            {
                RemoveEntity(change.EntityId);
            }
        }
    }

    public void RemoveEntity(EntityId entityId)
    {
        _entityOleBytes.Remove(entityId);
        _cache.Remove(Direct2DOleRenderKey.ForEntity(entityId));
    }

    public void CompleteFrame()
    {
        _cache.CompleteFrame();
        _suppressDrawDuringFrame = false;
    }

    public void Clear()
    {
        _entityOleBytes.Clear();
        _activeTransientKeys.Clear();
        _cachedTransientKeys.Clear();
        _staleTransientKeys.Clear();
        _reconciledTransientScene = null;
        _reconciledTransientVersion = -1;
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

        SetCacheEntry(key, replacement);
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

            SetCacheEntry(key, initial);
            DrawTiles(context, full, initial, tiles, opacity);
            return;
        }

        if (active.CanReuseFor(size.Width, size.Height))
        {
            var tiles = ResolveVisibleTiles(full, visible, active);
            if (ContainsAllTiles(active, tiles))
            {
                DrawTiles(context, full, active, tiles, opacity);
                active.RetainTiles(tiles);
                return;
            }

            if (!allowDraw)
            {
                DrawTiles(context, full, active, tiles, opacity);
                return;
            }

            _ = TryPopulateTiles(context, key, bytes, active, tiles);
            DrawTiles(context, full, active, tiles, opacity);
            active.RetainTiles(tiles);
            return;
        }

        if (!allowDraw)
        {
            DrawTiles(
                context,
                full,
                active,
                ResolveVisibleTiles(full, visible, active),
                opacity);
            return;
        }

        var replacement = new Direct2DOleBitmapCache.Entry(size.Width, size.Height);
        var replacementTiles = ResolveVisibleTiles(full, visible, replacement);
        if (!TryPopulateTiles(context, key, bytes, replacement, replacementTiles))
        {
            replacement.Dispose();
            DrawTiles(
                context,
                full,
                active,
                ResolveVisibleTiles(full, visible, active),
                opacity);
            return;
        }

        SetCacheEntry(key, replacement);
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
            statistics.RecordOleTileBuild();
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
        Span<Vector2> points =
        [
            Vector2.Transform(new Vector2((float)bounds.MinX, (float)bounds.MaxY), transform),
            Vector2.Transform(new Vector2((float)bounds.MaxX, (float)bounds.MaxY), transform),
            Vector2.Transform(new Vector2((float)bounds.MinX, (float)bounds.MinY), transform),
            Vector2.Transform(new Vector2((float)bounds.MaxX, (float)bounds.MinY), transform)
        ];
        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = minX;
        var maxY = minY;
        for (var index = 1; index < points.Length; index++)
        {
            minX = Math.Min(minX, points[index].X);
            minY = Math.Min(minY, points[index].Y);
            maxX = Math.Max(maxX, points[index].X);
            maxY = Math.Max(maxY, points[index].Y);
        }

        return new RawRectF(minX, minY, maxX, maxY);
    }

    private byte[] GetEntityBytes(CadOleObject ole)
    {
        if (_entityOleBytes.TryGetValue(ole.Id, out var bytes))
            return bytes;

        bytes = ole.CopyOleBytes();
        _entityOleBytes.Add(ole.Id, bytes);
        return bytes;
    }

    private void SetCacheEntry(
        Direct2DOleRenderKey key,
        Direct2DOleBitmapCache.Entry entry)
    {
        _cache.Set(key, entry);
        if (key.IsTransient)
            _cachedTransientKeys.Add(key);
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

    private IReadOnlySet<Direct2DOleBitmapCache.TileKey> ResolveVisibleTiles(
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
        _visibleTileKeys.Clear();
        for (var row = minY / TilePixelSide; row <= (maxY - 1) / TilePixelSide; row++)
        for (var column = minX / TilePixelSide; column <= (maxX - 1) / TilePixelSide; column++)
            _visibleTileKeys.Add(new Direct2DOleBitmapCache.TileKey(column, row));
        return _visibleTileKeys;
    }

    private static bool ContainsAllTiles(
        Direct2DOleBitmapCache.Entry entry,
        IReadOnlySet<Direct2DOleBitmapCache.TileKey> tiles)
    {
        foreach (var key in tiles)
        {
            if (!entry.Tiles.ContainsKey(key))
                return false;
        }

        return true;
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

    private static CadColor ResolveLayerColor(CadDocument document, LayerId layerId)
    {
        return document.TryGetLayer(layerId, out var layer) && layer is not null
            ? layer.Color
            : CadColor.FromRgb(128, 128, 128);
    }
}
