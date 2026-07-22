using System.Diagnostics;
using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Scene;

/// <summary>
/// Retains ordered, static entity runs as Direct2D command lists. Dynamic runs stay in the
/// normal entity path so draw order remains identical to immediate-mode rendering.
/// </summary>
internal sealed class Direct2DCommandListChunkCache : IDisposable
{
    private const int MinimumEntityCount = 1024;
    private const int EntitiesPerChunk = 384;
    private const int MaximumProfiles = 4;
    private const double BuildBudgetMilliseconds = 5.0;

    private readonly Direct2DResourceCache _resourceCache;
    private readonly Direct2DEntityOrderCache _entityOrderCache;
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private readonly Dictionary<RenderProfileKey, RenderProfile> _profiles = [];
    private CadDocument? _document;
    private long _usageStamp;
    private bool _disposed;

    public Direct2DCommandListChunkCache(
        Direct2DResourceCache resourceCache,
        Direct2DEntityOrderCache entityOrderCache,
        Direct2DRenderStatisticsCollector statistics)
    {
        _resourceCache = resourceCache;
        _entityOrderCache = entityOrderCache;
        _statistics = statistics;
    }

    public bool Prepare(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> orderedEntities,
        int estimatedRenderWork,
        Action<ID2D1DeviceContext, CadDocument, CadEntity, CadViewport, CadRenderOptions> drawEntity,
        bool buildStep)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        if (!CanUse(options, estimatedRenderWork))
            return false;

        if (!buildStep && !_profiles.ContainsKey(RenderProfileKey.Create(options, viewport.Zoom)))
            return true;

        var key = RenderProfileKey.Create(options, viewport.Zoom);
        if (!_profiles.TryGetValue(key, out var profile))
        {
            profile = BuildProfile(document, key, orderedEntities);
            _profiles.Add(key, profile);
            TrimProfiles(key);
        }

        profile.LastUsed = ++_usageStamp;
        if (!buildStep)
        {
            return profile.Chunks.Any(static chunk =>
                chunk.IsCacheable && chunk.CommandList is null && !chunk.BuildFailed);
        }

        var buildOptions = CreateBuildOptions(options, viewport.Zoom);
        var started = Stopwatch.GetTimestamp();
        foreach (var chunk in profile.Chunks)
        {
            if (!chunk.IsCacheable || chunk.CommandList is not null || chunk.BuildFailed)
                continue;

            chunk.CommandList = RecordChunk(
                context,
                document,
                viewport,
                buildOptions,
                chunk,
                drawEntity);
            if (chunk.CommandList is not null)
                _statistics.RecordCommandListBuild();
            else
                chunk.BuildFailed = true;

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= BuildBudgetMilliseconds)
                break;
        }

        return profile.Chunks.Any(static chunk =>
            chunk.IsCacheable && chunk.CommandList is null && !chunk.BuildFailed);
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Action<ID2D1DeviceContext, CadDocument, CadEntity, CadViewport, CadRenderOptions> drawEntity)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        var key = RenderProfileKey.Create(options, viewport.Zoom);
        if (options.ActiveLayoutId is not null ||
            !_profiles.TryGetValue(key, out var profile))
        {
            return false;
        }

        profile.LastUsed = ++_usageStamp;
        var renderBounds = Direct2DEntityVisibility.ResolveRenderWorldBounds(viewport, options);
        foreach (var chunk in profile.Chunks)
        {
            if (!IntersectsRenderBounds(chunk.Bounds, renderBounds, viewport.Zoom))
                continue;

            if (chunk.CommandList is not null && !ContainsHiddenDependency(chunk, options.HiddenEntityIds))
            {
                context.DrawImage(
                    chunk.CommandList,
                    null,
                    null,
                    InterpolationMode.Linear,
                    CompositeMode.SourceOver);
                _statistics.RecordCommandListReplay();
                _statistics.RecordVisibleEntities(chunk.RecordedEntityCount);
                continue;
            }

            foreach (var entity in Direct2DEntityVisibility.EnumerateOrderedSubset(
                         document,
                         viewport,
                         options,
                         _resourceCache,
                         chunk.Entities,
                         renderBounds))
            {
                _statistics.RecordVisibleEntity();
                _statistics.RecordEntitySubmission();
                _statistics.RecordFallbackEntity();
                drawEntity(context, document, entity, viewport, options);
            }
        }

        return true;
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        EnsureDocument(document);
        if (AffectsChunkPlan(document, changes))
        {
            ClearProfiles();
            return;
        }

        foreach (var change in changes.EntityChanges)
        {
            foreach (var profile in _profiles.Values)
                profile.Invalidate(change.EntityId);
        }
    }

    public void InvalidateEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        foreach (var profile in _profiles.Values)
            profile.Invalidate(entityId);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        ClearProfiles();
        _document = null;
    }

    private RenderProfile BuildProfile(
        CadDocument document,
        RenderProfileKey key,
        IReadOnlyList<CadEntity> orderedEntities)
    {
        var chunks = new List<RenderChunk>();
        var pending = new List<CadEntity>(EntitiesPerChunk);
        var blockCacheability = new Dictionary<BlockId, bool>();
        var blockDependencies = new Dictionary<BlockId, IReadOnlyList<EntityId>>();
        var visitingBlocks = new HashSet<BlockId>();

        void FlushCacheable()
        {
            if (pending.Count == 0)
                return;

            chunks.Add(CreateChunk(
                document,
                pending.ToArray(),
                isCacheable: true,
                blockDependencies));
            pending.Clear();
        }

        foreach (var entity in orderedEntities)
        {
            visitingBlocks.Clear();
            if (IsCacheable(document, entity, blockCacheability, visitingBlocks))
            {
                pending.Add(entity);
                if (pending.Count >= EntitiesPerChunk)
                    FlushCacheable();
                continue;
            }

            FlushCacheable();
            chunks.Add(CreateChunk(
                document,
                [entity],
                isCacheable: false,
                blockDependencies));
        }

        FlushCacheable();
        return new RenderProfile(key, chunks);
    }

    private RenderChunk CreateChunk(
        CadDocument document,
        IReadOnlyList<CadEntity> entities,
        bool isCacheable,
        Dictionary<BlockId, IReadOnlyList<EntityId>> blockDependencies)
    {
        var bounds = CadRectD.Empty;
        var dependencies = new HashSet<EntityId>();
        foreach (var entity in entities)
        {
            bounds = bounds.Union(entity.Bounds);
            dependencies.Add(entity.Id);
            if (entity is not CadBlockReference reference)
                continue;

            foreach (var dependency in ResolveBlockDependencies(
                         document,
                         reference.DefinitionBlockId,
                         blockDependencies,
                         []))
            {
                dependencies.Add(dependency);
            }
        }

        return new RenderChunk(entities, dependencies.ToArray(), bounds, isCacheable);
    }

    private bool IsCacheable(
        CadDocument document,
        CadEntity entity,
        Dictionary<BlockId, bool> blockCacheability,
        HashSet<BlockId> visitingBlocks)
    {
        if (entity is CadOleObject)
            return false;
        if (entity is not CadBlockReference reference)
        {
            return _resourceCache.TryGetEntityResources(entity.Id, out var resources) &&
                   resources is not null &&
                   resources.HatchBrush is null;
        }
        if (blockCacheability.TryGetValue(reference.DefinitionBlockId, out var cached))
            return cached;
        if (!visitingBlocks.Add(reference.DefinitionBlockId))
            return false;

        try
        {
            if (!document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
                definition is null)
            {
                return false;
            }

            var cacheable = true;
            foreach (var child in _entityOrderCache.GetOrderedEntities(document, definition.Id))
            {
                if (!IsCacheable(document, child, blockCacheability, visitingBlocks))
                {
                    cacheable = false;
                    break;
                }
            }

            blockCacheability[reference.DefinitionBlockId] = cacheable;
            return cacheable;
        }
        finally
        {
            visitingBlocks.Remove(reference.DefinitionBlockId);
        }
    }

    private IReadOnlyList<EntityId> ResolveBlockDependencies(
        CadDocument document,
        BlockId blockId,
        Dictionary<BlockId, IReadOnlyList<EntityId>> cache,
        HashSet<BlockId> visitingBlocks)
    {
        if (cache.TryGetValue(blockId, out var cached))
            return cached;
        if (!visitingBlocks.Add(blockId))
            return [];

        var dependencies = new HashSet<EntityId>();
        foreach (var child in _entityOrderCache.GetOrderedEntities(document, blockId))
        {
            dependencies.Add(child.Id);
            if (child is not CadBlockReference nested)
                continue;
            foreach (var dependency in ResolveBlockDependencies(
                         document,
                         nested.DefinitionBlockId,
                         cache,
                         visitingBlocks))
            {
                dependencies.Add(dependency);
            }
        }

        visitingBlocks.Remove(blockId);
        cached = dependencies.ToArray();
        cache[blockId] = cached;
        return cached;
    }

    private ID2D1CommandList? RecordChunk(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        RenderChunk chunk,
        Action<ID2D1DeviceContext, CadDocument, CadEntity, CadViewport, CadRenderOptions> drawEntity)
    {
        var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        var commandList = context.CreateCommandList();
        using var realizationScaleScope =
            _resourceCache.PushGeometryRealizationScale(viewport.Zoom);
        var isDrawing = false;
        var completed = false;
        try
        {
            context.Target = commandList;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = options.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = options.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;

            var recordedCount = 0;
            foreach (var entity in chunk.Entities)
            {
                if (!IsVisibleForRecording(document, entity))
                    continue;
                drawEntity(context, document, entity, viewport, options);
                recordedCount++;
            }

            var result = context.EndDraw();
            isDrawing = false;
            if (result.Failure)
                return null;

            context.Target = previousTarget;
            commandList.Close();
            chunk.RecordedEntityCount = recordedCount;
            completed = true;
            return commandList;
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
                commandList.Dispose();
        }
    }

    private static bool IsVisibleForRecording(CadDocument document, CadEntity entity)
    {
        return !entity.IsErased &&
               entity.IsVisible &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is { IsVisible: true, IsFrozen: false };
    }

    private static CadRenderOptions CreateBuildOptions(
        CadRenderOptions options,
        double viewportZoom) => new()
    {
        ActiveOwnerBlockId = options.ActiveOwnerBlockId,
        DrawGrid = false,
        DrawOrigin = false,
        DrawGripHandles = false,
        IsAntialiasingEnabled = options.IsAntialiasingEnabled,
        IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
        IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
        AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
        TransformScaleMultiplier = viewportZoom,
        KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
        HiddenEntityIds = CadRenderOptions.NoHiddenEntities
    };

    private static bool CanUse(CadRenderOptions options, int entityCount)
    {
        return options.ActiveLayoutId is null &&
               entityCount >= MinimumEntityCount;
    }

    private static bool IntersectsRenderBounds(
        CadRectD chunkBounds,
        CadRectD? renderBounds,
        double zoom)
    {
        if (renderBounds is null || chunkBounds.IsEmpty)
            return true;

        var padding = 64.0 / Math.Max(zoom, double.Epsilon);
        var paintBounds = chunkBounds.Inflate(padding);
        return paintBounds.Intersects(renderBounds.Value) ||
               paintBounds.Contains(renderBounds.Value.Center) ||
               renderBounds.Value.Contains(paintBounds);
    }

    private static bool ContainsHiddenDependency(
        RenderChunk chunk,
        IReadOnlySet<EntityId> hiddenEntityIds)
    {
        if (hiddenEntityIds.Count == 0)
            return false;
        foreach (var entityId in chunk.DependencyEntityIds)
        {
            if (hiddenEntityIds.Contains(entityId))
                return true;
        }

        return false;
    }

    private static bool AffectsChunkPlan(
        CadDocument document,
        CadDocumentChangeSet changes)
    {
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings)
            return true;

        const CadEntityChangeKind planChanges =
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.DrawOrder |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.Fill;
        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & planChanges) != 0)
                return true;
            if ((change.Kind & (CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata)) ==
                (CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata) &&
                document.TryGetEntity(change.EntityId, out var entity) &&
                entity is CadBlockReference)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureDocument(CadDocument document)
    {
        if (ReferenceEquals(_document, document))
            return;
        ClearProfiles();
        _document = document;
    }

    private void TrimProfiles(RenderProfileKey currentKey)
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

    public void Dispose()
    {
        if (_disposed)
            return;
        ClearProfiles();
        _document = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DCommandListChunkCache));
    }

    private readonly record struct RenderProfileKey(
        BlockId OwnerBlockId,
        long ZoomBits,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits)
    {
        public static RenderProfileKey Create(CadRenderOptions options, double zoom)
        {
            var quantizedZoom = QuantizeZoom(zoom);
            return new RenderProfileKey(
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
    }

    private sealed class RenderProfile : IDisposable
    {
        private readonly Dictionary<EntityId, List<RenderChunk>> _chunksByDependency = [];

        public RenderProfileKey Key { get; }
        public IReadOnlyList<RenderChunk> Chunks { get; }
        public long LastUsed { get; set; }

        public RenderProfile(RenderProfileKey key, IReadOnlyList<RenderChunk> chunks)
        {
            Key = key;
            Chunks = chunks;
            foreach (var chunk in chunks)
            {
                foreach (var entityId in chunk.DependencyEntityIds)
                {
                    if (!_chunksByDependency.TryGetValue(entityId, out var dependentChunks))
                    {
                        dependentChunks = [];
                        _chunksByDependency.Add(entityId, dependentChunks);
                    }

                    dependentChunks.Add(chunk);
                }
            }
        }

        public void Invalidate(EntityId entityId)
        {
            if (!_chunksByDependency.TryGetValue(entityId, out var chunks))
                return;
            foreach (var chunk in chunks)
                chunk.Invalidate();
        }

        public void Dispose()
        {
            foreach (var chunk in Chunks)
                chunk.Dispose();
            _chunksByDependency.Clear();
        }
    }

    private sealed class RenderChunk : IDisposable
    {
        public IReadOnlyList<CadEntity> Entities { get; }
        public IReadOnlyList<EntityId> DependencyEntityIds { get; }
        public CadRectD Bounds { get; private set; }
        public bool IsCacheable { get; }
        public ID2D1CommandList? CommandList { get; set; }
        public int RecordedEntityCount { get; set; }
        public bool BuildFailed { get; set; }

        public RenderChunk(
            IReadOnlyList<CadEntity> entities,
            IReadOnlyList<EntityId> dependencyEntityIds,
            CadRectD bounds,
            bool isCacheable)
        {
            Entities = entities;
            DependencyEntityIds = dependencyEntityIds;
            Bounds = bounds;
            IsCacheable = isCacheable;
        }

        public void Invalidate()
        {
            CommandList?.Dispose();
            CommandList = null;
            RecordedEntityCount = 0;
            BuildFailed = false;
            Bounds = CadRectD.Empty;
            foreach (var entity in Entities)
                Bounds = Bounds.Union(entity.Bounds);
        }

        public void Dispose() => Invalidate();
    }
}
