using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Scene;

/// <summary>
/// Rasterizes the static model scene into screen-density tiles. Command lists reduce CPU
/// submission cost; these tiles additionally avoid repeating the same GPU raster work.
/// </summary>
internal sealed class Direct2DSceneTileCache : IDisposable
{
    private const int MinimumEntityCount = 1024;
    private const int TilePixelSize = 512;
    private const int TileGutterPixels = 1;
    private const int TileBitmapPixelSize = TilePixelSize + TileGutterPixels * 2;
    private const int MaximumProfiles = 3;
    private const int MaximumTilesPerProfile = 64;
    private const int MaximumTilesTotal = 128;
    private const int MaximumFailedTilesPerProfile = 64;
    private const double MaximumFallbackZoomRatio = 2.0;
    private const int MaximumMissingTilesForPartialReplay = 1;
    private const double MinimumCoverageForPartialReplay = 0.75;

    private readonly Direct2DRenderStatisticsCollector _statistics;
    private readonly Dictionary<TileProfileKey, TileProfile> _profiles = [];
    private readonly Dictionary<EntityId, EntitySnapshot> _entitySnapshots = [];
    private readonly List<TileCoordinate> _requiredTiles = new(16);
    private readonly List<TileCoordinate> _availableTiles = new(16);
    private readonly List<CadRectD> _missingWorldBounds = new(4);
    private readonly List<TileProfile> _candidateProfiles = new(MaximumProfiles);
    private CadDocument? _document;
    private long _usageStamp;
    private bool _disposed;

    public Direct2DSceneTileCache(Direct2DRenderStatisticsCollector statistics)
    {
        _statistics = statistics;
    }

    public bool Prepare(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        int estimatedRenderWork,
        Func<ID2D1DeviceContext, CadDocument, CadViewport, CadRenderOptions, bool> drawScene,
        bool buildStep)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        if (!CanUse(options, estimatedRenderWork))
            return false;

        var key = TileProfileKey.Create(options, viewport.Zoom);
        if (!buildStep && !_profiles.ContainsKey(key))
            return true;

        if (!_profiles.TryGetValue(key, out var profile))
        {
            profile = new TileProfile(key, key.Zoom);
            _profiles.Add(key, profile);
            TrimProfiles(key);
        }

        profile.LastUsed = ++_usageStamp;
        FillVisibleTiles(viewport, profile.Zoom, _requiredTiles);
        if (!buildStep)
            return HasPendingTiles(profile, _requiredTiles);

        foreach (var tileKey in _requiredTiles)
        {
            if (profile.Tiles.ContainsKey(tileKey) || profile.FailedTiles.Contains(tileKey))
                continue;

            var tile = BuildTile(
                context,
                document,
                profile.Zoom,
                tileKey,
                options,
                drawScene);
            if (tile is null)
            {
                profile.FailedTiles.Add(tileKey);
                profile.TrimFailedTiles(MaximumFailedTilesPerProfile);
            }
            else
            {
                tile.LastUsed = ++_usageStamp;
                profile.Tiles.Add(tileKey, tile);
                profile.TrimTiles(MaximumTilesPerProfile, _requiredTiles);
                TrimTilesGlobally(profile, _requiredTiles);
                _statistics.RecordTileBuild();
            }
            break;
        }

        return HasPendingTiles(profile, _requiredTiles);
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadRenderOptions options,
        out IReadOnlyList<CadRectD> missingWorldBounds)
    {
        ThrowIfDisposed();
        missingWorldBounds = [];
        if (options.ActiveLayoutId is not null || options.HiddenEntityIds.Count > 0)
            return false;

        var key = TileProfileKey.Create(options, viewport.Zoom);
        if (!TryResolveDrawableProfile(
                key,
                viewport,
                options.AllowApproximateScaleFallback,
                out var profile,
                out var requiredTiles,
                out var availableTiles))
        {
            return false;
        }

        _missingWorldBounds.Clear();
        foreach (var tileKey in requiredTiles)
        {
            if (!profile.Tiles.ContainsKey(tileKey))
                _missingWorldBounds.Add(ResolveWorldBounds(tileKey, profile.Zoom));
        }
        missingWorldBounds = _missingWorldBounds;

        profile.LastUsed = ++_usageStamp;
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.Identity;
        try
        {
            foreach (var tileKey in availableTiles)
            {
                var tile = profile.Tiles[tileKey];
                tile.LastUsed = ++_usageStamp;
                var topLeft = viewport.WorldToScreen(new CadPointD(tile.WorldBounds.MinX, tile.WorldBounds.MaxY));
                var size = tile.WorldBounds.Width * viewport.Zoom;
                var nominalDestination = new RawRectF(
                    (float)topLeft.X,
                    (float)topLeft.Y,
                    (float)(topLeft.X + size),
                    (float)(topLeft.Y + size));
                var gutterSize = TileGutterPixels * viewport.Zoom / profile.Zoom;
                var destination = new RawRectF(
                    (float)(topLeft.X - gutterSize),
                    (float)(topLeft.Y - gutterSize),
                    (float)(topLeft.X + size + gutterSize),
                    (float)(topLeft.Y + size + gutterSize));
                var source = new RawRectF(
                    0,
                    0,
                    TileBitmapPixelSize,
                    TileBitmapPixelSize);

                context.PushAxisAlignedClip(nominalDestination, AntialiasMode.Aliased);
                try
                {
                    context.DrawBitmap(
                        tile.Bitmap,
                        destination,
                        1.0f,
                        InterpolationMode.Linear,
                        source,
                        null);
                }
                finally
                {
                    context.PopAxisAlignedClip();
                }
                _statistics.RecordTileReplay();
            }
        }
        finally
        {
            context.Transform = previousTransform;
        }

        return true;
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings)
        {
            ClearProfiles();
            CaptureEntitySnapshots(document);
            return;
        }

        var affectedBlocks = new HashSet<BlockId>();
        foreach (var change in changes.EntityChanges)
            InvalidateChangedEntity(document, change, affectedBlocks);

        if (affectedBlocks.Count > 0)
            InvalidateBlockInstances(document, affectedBlocks);
    }

    public void InvalidateEntity(CadDocument document, EntityId entityId)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        var affectedBlocks = new HashSet<BlockId>();
        InvalidateChangedEntity(
            document,
            new CadEntityChange(entityId, CadEntityChangeKind.Geometry),
            affectedBlocks);
        if (affectedBlocks.Count > 0)
            InvalidateBlockInstances(document, affectedBlocks);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        if (!_entitySnapshots.Remove(entityId, out var previous))
            return;

        InvalidateSnapshot(previous);
    }

    public void InvalidateEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        if (_entitySnapshots.TryGetValue(entityId, out var snapshot))
            InvalidateSnapshot(snapshot);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        ClearProfiles();
        _document = null;
        _entitySnapshots.Clear();
    }

    private SceneTile? BuildTile(
        ID2D1DeviceContext context,
        CadDocument document,
        double zoom,
        TileCoordinate tileKey,
        CadRenderOptions sourceOptions,
        Func<ID2D1DeviceContext, CadDocument, CadViewport, CadRenderOptions, bool> drawScene)
    {
        var worldBounds = ResolveWorldBounds(tileKey, zoom);
        var tileViewport = CreateTileViewport(worldBounds, zoom);
        var options = CreateTileOptions(sourceOptions, worldBounds, zoom);
        var bitmap = context.CreateBitmap(
            new SizeI(TileBitmapPixelSize, TileBitmapPixelSize),
            IntPtr.Zero,
            0,
            new BitmapProperties1
            {
                PixelFormat = new PixelFormat(
                    DXGIFormat.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Premultiplied),
                DpiX = 96.0f,
                DpiY = 96.0f,
                BitmapOptions = BitmapOptions.Target
            });

        var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        var isDrawing = false;
        var completed = false;
        try
        {
            context.Target = bitmap;
            context.Transform = Matrix3x2.CreateScale((float)zoom, (float)-zoom) *
                                Matrix3x2.CreateTranslation(
                                    (float)(-worldBounds.MinX * zoom + TileGutterPixels),
                                    (float)(worldBounds.MaxY * zoom + TileGutterPixels));
            context.AntialiasMode = options.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = options.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;
            context.Clear(new Color4(0, 0, 0, 0));
            if (!drawScene(context, document, tileViewport, options))
                return null;

            var result = context.EndDraw();
            isDrawing = false;
            if (result.Failure)
                return null;

            completed = true;
            return new SceneTile(bitmap, worldBounds);
        }
        finally
        {
            if (isDrawing)
                context.EndDraw();
            context.Target = previousTarget;
            context.PrimitiveBlend = previousPrimitiveBlend;
            context.TextAntialiasMode = previousTextAntialiasMode;
            context.AntialiasMode = previousAntialiasMode;
            context.Transform = previousTransform;
            if (!completed)
                bitmap.Dispose();
        }
    }

    private static void FillVisibleTiles(
        CadViewport viewport,
        double profileZoom,
        List<TileCoordinate> result)
    {
        result.Clear();
        var visible = viewport.VisibleWorldBounds;
        if (visible.IsEmpty || profileZoom <= double.Epsilon)
            return;

        var worldSize = TilePixelSize / profileZoom;
        var minX = FloorToInt(visible.MinX / worldSize);
        var maxX = FloorToInt(Math.BitDecrement(visible.MaxX) / worldSize);
        var minY = FloorToInt(visible.MinY / worldSize);
        var maxY = FloorToInt(Math.BitDecrement(visible.MaxY) / worldSize);
        var requiredCapacity = Math.Max(1, (maxX - minX + 1) * (maxY - minY + 1));
        if (result.Capacity < requiredCapacity)
            result.Capacity = requiredCapacity;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
                result.Add(new TileCoordinate(x, y));
        }
    }

    private static CadRectD ResolveWorldBounds(TileCoordinate coordinate, double zoom)
    {
        var worldSize = TilePixelSize / zoom;
        var minX = coordinate.X * worldSize;
        var minY = coordinate.Y * worldSize;
        return CadRectD.FromXYWH(minX, minY, worldSize, worldSize);
    }

    private static CadViewport CreateTileViewport(CadRectD worldBounds, double zoom)
    {
        var viewport = new CadViewport();
        viewport.SetSize(TilePixelSize, TilePixelSize);
        viewport.SetView(zoom, new CadPointD(
            -worldBounds.MinX * zoom,
            worldBounds.MaxY * zoom));
        return viewport;
    }

    private static CadRenderOptions CreateTileOptions(
        CadRenderOptions source,
        CadRectD worldBounds,
        double zoom) => new()
    {
        ActiveOwnerBlockId = source.ActiveOwnerBlockId,
        DrawGrid = false,
        DrawOrigin = false,
        DrawGripHandles = false,
        IsAntialiasingEnabled = source.IsAntialiasingEnabled,
        IsTextAntialiasingEnabled = source.IsTextAntialiasingEnabled,
        IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
        AllowApproximateScaleFallback = source.AllowApproximateScaleFallback,
        TransformScaleMultiplier = source.TransformScaleMultiplier,
        KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
        HiddenEntityIds = CadRenderOptions.NoHiddenEntities,
        DirtyWorldBounds = worldBounds.Inflate(TileGutterPixels / zoom),
        EntityBoundsQuery = source.EntityBoundsQuery,
        EntityBoundsQueryInto = source.EntityBoundsQueryInto
    };

    private static bool CanUse(CadRenderOptions options, int entityCount)
    {
        return options.ActiveLayoutId is null &&
               entityCount >= MinimumEntityCount;
    }

    private void EnsureDocument(CadDocument document)
    {
        if (ReferenceEquals(_document, document))
            return;
        ClearProfiles();
        _document = document;
        CaptureEntitySnapshots(document);
    }

    private void InvalidateChangedEntity(
        CadDocument document,
        CadEntityChange change,
        ISet<BlockId> affectedBlocks)
    {
        const CadEntityChangeKind visualChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.DrawOrder |
            CadEntityChangeKind.Fill |
            CadEntityChangeKind.EmbeddedData |
            CadEntityChangeKind.Opacity |
            CadEntityChangeKind.Rotation;
        if ((change.Kind & visualChanges) == 0)
            return;

        _entitySnapshots.TryGetValue(change.EntityId, out var previous);
        var hasCurrent = document.TryGetEntity(change.EntityId, out var entity) && entity is not null;
        var current = hasCurrent
            ? CreateSnapshot(entity!)
            : default;

        if (previous.IsValid)
            InvalidateSnapshot(previous);
        if (current.IsValid)
            InvalidateSnapshot(current);

        if (previous.IsValid && !previous.OwnerBlockId.Equals(BlockId.ModelSpace))
            affectedBlocks.Add(previous.OwnerBlockId);
        if (current.IsValid && !current.OwnerBlockId.Equals(BlockId.ModelSpace))
            affectedBlocks.Add(current.OwnerBlockId);

        if (hasCurrent)
            _entitySnapshots[change.EntityId] = current;
        else
            _entitySnapshots.Remove(change.EntityId);

    }

    private void InvalidateSnapshot(EntitySnapshot snapshot)
    {
        if (!snapshot.IsValid)
            return;

        foreach (var profile in _profiles.Values)
        {
            if (!profile.Key.OwnerBlockId.Equals(snapshot.OwnerBlockId))
                continue;
            profile.Invalidate(snapshot.Bounds);
        }
    }

    private void CaptureEntitySnapshots(CadDocument document)
    {
        _entitySnapshots.Clear();
        foreach (var entity in document.Entities.Values)
        {
            _entitySnapshots[entity.Id] = new EntitySnapshot(
                entity.OwnerBlockId,
                entity.Bounds,
                Exists: true);
        }
    }

    private void InvalidateBlockInstances(
        CadDocument document,
        IReadOnlyCollection<BlockId> changedBlockIds)
    {
        var referencesByDefinition = document.Entities.Values
            .OfType<CadBlockReference>()
            .Where(static reference => !reference.IsErased)
            .GroupBy(static reference => reference.DefinitionBlockId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var pending = new Queue<BlockId>(changedBlockIds);
        var visited = new HashSet<BlockId>();
        while (pending.TryDequeue(out var changedBlockId))
        {
            if (!visited.Add(changedBlockId) ||
                !referencesByDefinition.TryGetValue(changedBlockId, out var references))
            {
                continue;
            }

            foreach (var reference in references)
            {
                _entitySnapshots.TryGetValue(reference.Id, out var previous);
                var current = CreateSnapshot(reference);
                if (previous.IsValid)
                    InvalidateSnapshot(previous);
                InvalidateSnapshot(current);
                _entitySnapshots[reference.Id] = current;

                if (!reference.OwnerBlockId.Equals(BlockId.ModelSpace))
                    pending.Enqueue(reference.OwnerBlockId);
            }
        }
    }

    private bool TryResolveDrawableProfile(
        TileProfileKey requestedKey,
        CadViewport viewport,
        bool allowApproximateScaleFallback,
        out TileProfile profile,
        out IReadOnlyList<TileCoordinate> requiredTiles,
        out IReadOnlyList<TileCoordinate> availableTiles)
    {
        FillCandidateProfiles(requestedKey, viewport.Zoom, allowApproximateScaleFallback);
        foreach (var candidate in _candidateProfiles)
        {
            FillVisibleTiles(viewport, candidate.Zoom, _requiredTiles);
            _availableTiles.Clear();
            foreach (var tileKey in _requiredTiles)
            {
                if (candidate.Tiles.ContainsKey(tileKey))
                    _availableTiles.Add(tileKey);
            }

            if (_availableTiles.Count == 0)
                continue;

            var missingTileCount = _requiredTiles.Count - _availableTiles.Count;
            var isExactProfile = candidate.Key.Equals(requestedKey);
            if (!isExactProfile && missingTileCount > 0)
                continue;

            var coverage = (double)_availableTiles.Count / _requiredTiles.Count;
            if (isExactProfile &&
                missingTileCount > 0 &&
                (missingTileCount > MaximumMissingTilesForPartialReplay ||
                 coverage < MinimumCoverageForPartialReplay))
            {
                continue;
            }

            profile = candidate;
            requiredTiles = _requiredTiles;
            availableTiles = _availableTiles;
            return true;
        }

        profile = null!;
        requiredTiles = [];
        availableTiles = [];
        return false;
    }

    private void FillCandidateProfiles(
        TileProfileKey requestedKey,
        double zoom,
        bool allowApproximateScaleFallback)
    {
        _candidateProfiles.Clear();
        _profiles.TryGetValue(requestedKey, out var exact);
        if (exact is { Tiles.Count: > 0 })
            _candidateProfiles.Add(exact);

        if (!allowApproximateScaleFallback)
            return;

        var fallbackStart = _candidateProfiles.Count;
        foreach (var candidate in _profiles.Values)
        {
            if (ReferenceEquals(candidate, exact) ||
                candidate.Tiles.Count == 0 ||
                !candidate.Key.IsCompatibleWith(requestedKey) ||
                ResolveZoomRatio(candidate.Zoom, zoom) > MaximumFallbackZoomRatio)
            {
                continue;
            }

            var candidateDistance = ResolveProfileDistance(candidate.Zoom, zoom);
            var insertIndex = _candidateProfiles.Count;
            while (insertIndex > fallbackStart &&
                   ResolveProfileDistance(_candidateProfiles[insertIndex - 1].Zoom, zoom) > candidateDistance)
            {
                insertIndex--;
            }
            _candidateProfiles.Insert(insertIndex, candidate);
        }
    }

    private static bool HasPendingTiles(
        TileProfile profile,
        IReadOnlyList<TileCoordinate> requiredTiles)
    {
        foreach (var tileKey in requiredTiles)
        {
            if (!profile.Tiles.ContainsKey(tileKey) && !profile.FailedTiles.Contains(tileKey))
                return true;
        }

        return false;
    }

    private static double ResolveProfileDistance(double profileZoom, double requestedZoom) =>
        Math.Abs(Math.Log2(profileZoom / Math.Max(requestedZoom, 1e-9)));

    private static double ResolveZoomRatio(double left, double right) =>
        Math.Max(left, right) / Math.Max(Math.Min(left, right), 1e-9);

    private static EntitySnapshot CreateSnapshot(CadEntity entity) => new(
        entity.OwnerBlockId,
        entity.Bounds,
        Exists: true);

    private void TrimTilesGlobally(
        TileProfile protectedProfile,
        IReadOnlyCollection<TileCoordinate> protectedTiles)
    {
        var overflow = _profiles.Values.Sum(static profile => profile.Tiles.Count) -
                       MaximumTilesTotal;
        if (overflow <= 0)
            return;

        var protectedSet = protectedTiles as IReadOnlySet<TileCoordinate> ??
                           protectedTiles.ToHashSet();
        var candidates = _profiles.Values
            .SelectMany(profile => profile.Tiles.Select(pair => new
            {
                Profile = profile,
                Coordinate = pair.Key,
                Tile = pair.Value
            }))
            .Where(candidate =>
                !ReferenceEquals(candidate.Profile, protectedProfile) ||
                !protectedSet.Contains(candidate.Coordinate))
            .OrderBy(static candidate => candidate.Tile.LastUsed)
            .Take(overflow)
            .ToArray();
        foreach (var candidate in candidates)
            candidate.Profile.RemoveTile(candidate.Coordinate);
    }

    private void TrimProfiles(TileProfileKey currentKey)
    {
        while (_profiles.Count > MaximumProfiles)
        {
            var oldest = _profiles
                .Where(pair => !pair.Key.Equals(currentKey))
                .OrderBy(pair => pair.Value.LastUsed)
                .First();
            oldest.Value.Dispose();
            _profiles.Remove(oldest.Key);
        }
    }

    private void ClearProfiles()
    {
        foreach (var profile in _profiles.Values)
            profile.Dispose();
        _profiles.Clear();
    }

    private static int FloorToInt(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        return (int)Math.Clamp(Math.Floor(value), int.MinValue, int.MaxValue);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ClearProfiles();
        _document = null;
        _entitySnapshots.Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DSceneTileCache));
    }

    private readonly record struct TileCoordinate(int X, int Y);

    private readonly record struct EntitySnapshot(
        BlockId OwnerBlockId,
        CadRectD Bounds,
        bool Exists)
    {
        public bool IsValid => Exists && !Bounds.IsEmpty;
    }

    private readonly record struct TileProfileKey(
        BlockId OwnerBlockId,
        long ZoomBits,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits)
    {
        public double Zoom => BitConverter.Int64BitsToDouble(ZoomBits);

        public static TileProfileKey Create(CadRenderOptions options, double zoom)
        {
            var quantizedZoom = QuantizeZoom(zoom);
            return new TileProfileKey(
                options.ActiveOwnerBlockId,
                BitConverter.DoubleToInt64Bits(quantizedZoom),
                options.IsAntialiasingEnabled,
                options.IsTextAntialiasingEnabled,
                options.IsLevelOfDetailEnabled,
                options.KeepStrokeWidthScreenConstant,
                BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth));
        }

        private static double QuantizeZoom(double zoom)
        {
            zoom = Math.Max(zoom, 1e-9);
            return Math.Pow(2.0, Math.Round(Math.Log2(zoom) * 64.0) / 64.0);
        }

        public bool IsCompatibleWith(TileProfileKey other) =>
            OwnerBlockId.Equals(other.OwnerBlockId) &&
            IsAntialiasingEnabled == other.IsAntialiasingEnabled &&
            IsTextAntialiasingEnabled == other.IsTextAntialiasingEnabled &&
            IsLevelOfDetailEnabled == other.IsLevelOfDetailEnabled &&
            KeepStrokeWidthScreenConstant == other.KeepStrokeWidthScreenConstant &&
            MinimumScreenStrokeWidthBits == other.MinimumScreenStrokeWidthBits;
    }

    private sealed class TileProfile(TileProfileKey key, double zoom) : IDisposable
    {
        public TileProfileKey Key { get; } = key;
        public double Zoom { get; } = zoom;
        public Dictionary<TileCoordinate, SceneTile> Tiles { get; } = [];
        public HashSet<TileCoordinate> FailedTiles { get; } = [];
        public long LastUsed { get; set; }

        public void Dispose()
        {
            foreach (var tile in Tiles.Values)
                tile.Dispose();
            Tiles.Clear();
            FailedTiles.Clear();
        }

        public void TrimTiles(int maximumCount, IReadOnlyCollection<TileCoordinate> protectedTiles)
        {
            if (Tiles.Count <= maximumCount)
                return;

            var protectedSet = protectedTiles as IReadOnlySet<TileCoordinate> ??
                               protectedTiles.ToHashSet();
            foreach (var pair in Tiles
                         .Where(pair => !protectedSet.Contains(pair.Key))
                         .OrderBy(pair => pair.Value.LastUsed)
                         .Take(Tiles.Count - maximumCount)
                         .ToArray())
            {
                pair.Value.Dispose();
                Tiles.Remove(pair.Key);
            }
        }

        public void RemoveTile(TileCoordinate coordinate)
        {
            if (!Tiles.Remove(coordinate, out var tile))
                return;
            tile.Dispose();
        }

        public void TrimFailedTiles(int maximumCount)
        {
            while (FailedTiles.Count > maximumCount)
                FailedTiles.Remove(FailedTiles.First());
        }

        public void Invalidate(CadRectD entityBounds)
        {
            if (entityBounds.IsEmpty)
                return;

            var paintBounds = entityBounds.Inflate(64.0 / Math.Max(Zoom, double.Epsilon));
            foreach (var pair in Tiles
                         .Where(pair => Intersects(pair.Value.WorldBounds, paintBounds))
                         .ToArray())
            {
                pair.Value.Dispose();
                Tiles.Remove(pair.Key);
            }

            foreach (var tileKey in FailedTiles
                         .Where(tileKey => Intersects(ResolveWorldBounds(tileKey, Zoom), paintBounds))
                         .ToArray())
            {
                FailedTiles.Remove(tileKey);
            }
        }

        private static bool Intersects(CadRectD left, CadRectD right) =>
            left.Intersects(right) ||
            left.Contains(right.Center) ||
            right.Contains(left.Center);
    }

    private sealed class SceneTile(ID2D1Bitmap1 bitmap, CadRectD worldBounds) : IDisposable
    {
        public ID2D1Bitmap1 Bitmap { get; } = bitmap;
        public CadRectD WorldBounds { get; } = worldBounds;
        public long LastUsed { get; set; }
        public void Dispose() => Bitmap.Dispose();
    }
}
