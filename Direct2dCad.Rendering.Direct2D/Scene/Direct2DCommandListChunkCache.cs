using System.Diagnostics;
using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Handles;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Scene;

/// <summary>
/// Retains ordered, static entity runs as Direct2D command lists. Dynamic runs stay in the
/// normal entity path so draw order remains identical to immediate-mode rendering.
/// </summary>
internal sealed class Direct2DCommandListChunkCache : IDisposable
{
    private const int MinimumEntityCount = 1024;
    private const int MinimumSpatialChunkEntityCount = 64;
    private const int EntitiesPerChunk = 384;
    private const int MaximumProfiles = 4;
    private const double BuildBudgetMilliseconds = 5.0;
    private const double PlanBuildBudgetMilliseconds = 2.0;
    internal const long CacheBudgetBytes = 64L * 1024 * 1024;

    private readonly Direct2DResourceCache _resourceCache;
    private readonly Direct2DEntityOrderCache _entityOrderCache;
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private readonly Direct2DChunkRecordingWorker _backgroundWorker;
    private readonly Dictionary<long, RenderChunk> _backgroundChunks = [];
    private readonly Dictionary<RenderProfileKey, RenderProfile> _profiles = [];
    private readonly CacheEvictionQueue<RenderChunk> _evictionCandidates = new();
    private readonly Dictionary<BlockId, IReadOnlyList<RenderChunkPlan>> _chunkPlans = [];
    private readonly Dictionary<BlockId, RenderChunkPlanBuilder> _planBuilders = [];
    private readonly HashSet<BlockId> _invalidOwners = [];
    private ID2D1Factory? _backgroundFactory;
    private ID2D1Device? _backgroundDevice;
    private CadDocument? _document;
    private long _usageStamp;
    private long _estimatedBytes;
    private bool _disposed;

    public Direct2DCommandListChunkCache(
        Direct2DResourceCache resourceCache,
        Direct2DEntityOrderCache entityOrderCache,
        Direct2DRenderStatisticsCollector statistics)
    {
        _resourceCache = resourceCache;
        _entityOrderCache = entityOrderCache;
        _statistics = statistics;
        _backgroundWorker = new Direct2DChunkRecordingWorker(resourceCache);
    }

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);
    internal int LastInvalidatedChunkCount { get; private set; }

    public void ResetBackgroundResources(ID2D1Factory? factory, ID2D1Device? device)
    {
        ThrowIfDisposed();
        CancelBackgroundRecordings();
        _backgroundWorker.Reset(factory: null, device: null);
        _backgroundFactory = factory;
        _backgroundDevice = device;
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
        PublishCompletedBackgroundRecordings();
        if (!options.IsBackgroundChunkRecordingEnabled &&
            _backgroundWorker.IsReady)
        {
            CancelBackgroundRecordings();
            _backgroundWorker.Reset(factory: null, device: null);
        }
        if (_backgroundChunks.Count > 0 &&
            (!options.IsBackgroundChunkRecordingEnabled ||
             !_backgroundWorker.IsReady))
        {
            CancelBackgroundRecordings();
        }
        if (!CanUse(options, estimatedRenderWork))
            return false;

        if (!buildStep && !_profiles.ContainsKey(RenderProfileKey.Create(options, viewport.Zoom)))
            return true;

        var key = RenderProfileKey.Create(options, viewport.Zoom);
        if (!_profiles.TryGetValue(key, out var profile))
        {
            if (!_chunkPlans.TryGetValue(key.OwnerBlockId, out var chunkPlans))
            {
                if (!_planBuilders.TryGetValue(key.OwnerBlockId, out var planBuilder))
                {
                    planBuilder = new RenderChunkPlanBuilder(
                        this,
                        document,
                        key.OwnerBlockId,
                        orderedEntities);
                    _planBuilders.Add(key.OwnerBlockId, planBuilder);
                }

                if (!planBuilder.BuildStep(PlanBuildBudgetMilliseconds))
                    return true;

                chunkPlans = planBuilder.Plans;
                _chunkPlans.Add(key.OwnerBlockId, chunkPlans);
                _planBuilders.Remove(key.OwnerBlockId);
            }

            profile = BuildProfile(key, chunkPlans);
            _profiles.Add(key, profile);
            TrimProfiles(key);
        }

        profile.LastUsed = ++_usageStamp;
        if (!buildStep)
            return profile.HasPendingBuilds;

        var buildOptions = CreateBuildOptions(options, viewport.Zoom);
        var started = Stopwatch.GetTimestamp();
        var visibleWorldBounds = viewport.VisibleWorldBounds;
        var renderPadding = Direct2DEntityVisibility.ResolveBroadPhasePadding(
            _resourceCache,
            viewport,
            options);
        for (var pass = 0; pass < 2; pass++)
        {
            var buildVisibleChunks = pass == 0;
            foreach (var chunk in profile.Chunks)
            {
                if (!chunk.IsCacheable ||
                    chunk.CommandList is not null ||
                    chunk.PendingRecordingId != 0 ||
                    chunk.BuildFailed ||
                    chunk.WasBudgetEvicted ||
                    IntersectsRenderBounds(chunk.Bounds, visibleWorldBounds, renderPadding) !=
                    buildVisibleChunks)
                {
                    continue;
                }

                if (options.IsBackgroundChunkRecordingEnabled &&
                    EnsureBackgroundWorkerReady() &&
                    CanRecordInBackground(chunk))
                {
                    if (!_backgroundWorker.CanSchedule)
                        return true;
                    PrepareBackgroundResources(chunk, options);
                    var backgroundOptions = CreateBuildOptions(
                        options,
                        viewport.Zoom,
                        enableGeometryRealizations: false);
                    if (_backgroundWorker.TrySchedule(
                            document,
                            viewport,
                            backgroundOptions,
                            chunk.Entities,
                            out var requestId))
                    {
                        chunk.PendingRecordingId = requestId;
                        _backgroundChunks.Add(requestId, chunk);
                    }

                    // One worker owns one context. Keep foreground cache preparation responsive
                    // while that context records the selected chunk.
                    return true;
                }

                chunk.CommandList = RecordChunk(
                    context,
                    document,
                    viewport,
                    buildOptions,
                    chunk,
                    drawEntity);
                if (chunk.CommandList is not null)
                {
                    chunk.EstimatedBytes = EstimateCommandListBytes(chunk);
                    chunk.LastUsed = ++_usageStamp;
                    _statistics.RecordCommandListBuild();
                    TrimToBudget(chunk);
                }
                else
                    chunk.BuildFailed = true;

                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >=
                    BuildBudgetMilliseconds)
                {
                    return profile.HasPendingBuilds;
                }
            }
        }

        return profile.HasPendingBuilds;
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        CadHandleScene? handleScene,
        Action<ID2D1DeviceContext, CadDocument, CadEntity, CadViewport, CadRenderOptions> drawEntity,
        Action<ID2D1DeviceContext, CadDocument, CadViewport, CadSelectionEntityReference, CadRenderOptions> drawSelectionEntity)
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
        var renderPadding = Direct2DEntityVisibility.ResolveBroadPhasePadding(
            _resourceCache,
            viewport,
            options);
        if (options.IsLevelOfDetailEnabled &&
            profile.EntityCount >= 1024 &&
            profile.HasPendingBuilds)
        {
            return false;
        }

        foreach (var chunk in profile.Chunks)
        {
            if (!IntersectsRenderBounds(chunk.Bounds, renderBounds, renderPadding))
                continue;
            if (AreAllTopLevelEntitiesHidden(chunk, options.HiddenEntityIds) &&
                !ContainsInlineSelectionDependency(chunk, handleScene))
                continue;

            if (chunk.CommandList is not null &&
                !ContainsHiddenDependency(chunk, options.HiddenEntityIds) &&
                !ContainsInlineSelectionDependency(chunk, handleScene))
            {
                context.DrawImage(
                    chunk.CommandList,
                    null,
                    null,
                    InterpolationMode.Linear,
                    CompositeMode.SourceOver);
                _statistics.RecordCommandListReplay();
                _statistics.RecordVisibleEntities(chunk.RecordedEntityCount);
                chunk.LastUsed = ++_usageStamp;
                continue;
            }

            var fallbackStarted = Stopwatch.GetTimestamp();
            try
            {
                foreach (var entity in chunk.Entities)
                {
                    if (handleScene?.TryGetSelectionReference(entity.Id, out var reference) == true &&
                        reference is not null)
                    {
                        drawSelectionEntity(context, document, viewport, reference, options);
                        continue;
                    }

                    if (!Direct2DEntityVisibility.TryResolveVisibleEntity(
                            document,
                            viewport,
                            options,
                            _resourceCache,
                            entity,
                            renderBounds,
                            out var visible))
                    {
                        continue;
                    }

                    _statistics.RecordVisibleEntity();
                    _statistics.RecordEntitySubmission();
                    _statistics.RecordFallbackEntity();
                    drawEntity(context, document, visible.Entity, viewport, options);
                }
            }
            finally
            {
                _statistics.RecordCpuEntitySubmission(
                    Stopwatch.GetElapsedTime(fallbackStarted).TotalMilliseconds);
            }
        }

        return true;
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        LastInvalidatedChunkCount = 0;
        ThrowIfDisposed();
        EnsureDocument(document);
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings)
        {
            ClearChunkCaches();
            return;
        }

        const CadEntityChangeKind visualChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.EmbeddedData |
            CadEntityChangeKind.Opacity |
            CadEntityChangeKind.Rotation;
        const CadEntityChangeKind planChanges = CadEntityChangeKind.Created | CadEntityChangeKind.Deleted |
            CadEntityChangeKind.DrawOrder | CadEntityChangeKind.Layer | CadEntityChangeKind.Fill;
        if (!changes.EntityChanges.Any(change => (change.Kind & (visualChanges | planChanges)) != 0))
            return;
        // Resource updates follow this call and must not race the recorder.
        CancelBackgroundRecordings();
        _invalidOwners.Clear();
        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & planChanges) == 0 &&
                (change.Kind & (CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata)) !=
                (CadEntityChangeKind.Geometry | CadEntityChangeKind.Metadata))
                continue;
            if (!document.TryGetEntity(change.EntityId, out var entity) || entity is null)
            {
                ClearChunkCaches();
                return;
            }
            if ((change.Kind & planChanges) != 0 || entity is CadBlockReference)
                CollectDependentOwners(document, entity.OwnerBlockId);
        }
        foreach (var owner in _invalidOwners)
        {
            _chunkPlans.Remove(owner);
            _planBuilders.Remove(owner);
        }
        foreach (var key in _profiles.Keys.Where(key => _invalidOwners.Contains(key.OwnerBlockId)).ToArray())
        {
            _profiles[key].Dispose();
            _profiles.Remove(key);
        }
        _invalidOwners.Clear();
        foreach (var profile in _profiles.Values)
            LastInvalidatedChunkCount += profile.Invalidate(changes.EntityChanges, visualChanges);
    }

    public void InvalidateEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        CancelBackgroundRecordings();
        foreach (var profile in _profiles.Values)
            profile.Invalidate(entityId);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        CancelBackgroundRecordings();
        ClearChunkCaches();
        _document = null;
    }

    private RenderProfile BuildProfile(
        RenderProfileKey key,
        IReadOnlyList<RenderChunkPlan> plans)
    {
        var chunks = new RenderChunk[plans.Count];
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var bounds = CadRectD.Empty;
            foreach (var entity in plan.Entities)
                bounds = bounds.Union(entity.Bounds);
            chunks[index] = new RenderChunk(
                plan.Entities,
                plan.DependencyEntityIds,
                bounds,
                plan.IsCacheable,
                AdjustEstimatedBytes);
        }

        return new RenderProfile(key, chunks);
    }

    private RenderChunkPlan CreateChunkPlan(
        CadDocument document,
        IReadOnlyList<CadEntity> entities,
        bool isCacheable,
        Dictionary<BlockId, IReadOnlyList<EntityId>> blockDependencies)
    {
        var dependencies = new HashSet<EntityId>();
        foreach (var entity in entities)
        {
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

        return new RenderChunkPlan(
            entities,
            dependencies.ToArray(),
            isCacheable);
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

    private bool CanRecordInBackground(RenderChunk chunk)
    {
        foreach (var entity in chunk.Entities)
        {
            // Block-reference recording owns additional mutable caches. Keep those chunks on
            // the foreground path until block caches have their own worker-local recorder.
            if (entity is CadBlockReference ||
                !_resourceCache.TryGetEntityResources(entity.Id, out var resources) ||
                resources is null ||
                resources.HatchBrush is not null)
            {
                return false;
            }
        }

        return true;
    }

    private bool EnsureBackgroundWorkerReady()
    {
        if (_backgroundWorker.IsReady)
            return true;
        if (_backgroundFactory is null || _backgroundDevice is null)
            return false;

        try
        {
            _backgroundWorker.Reset(_backgroundFactory, _backgroundDevice);
            return _backgroundWorker.IsReady;
        }
        catch
        {
            // Multiple-context recording is an optimization. The foreground recorder remains
            // the correctness path when a driver rejects the additional device context.
            return false;
        }
    }

    private void PrepareBackgroundResources(RenderChunk chunk, CadRenderOptions options)
    {
        if (!options.IsLevelOfDetailEnabled)
            return;

        // LOD geometry creation mutates the entity bucket, so finish it on the foreground
        // thread before the worker starts reading the bucket.
        foreach (var entity in chunk.Entities)
        {
            if (_resourceCache.TryGetEntityResources(entity.Id, out var resources) &&
                resources is not null)
            {
                _resourceCache.EnsureLevelOfDetailGeometries(entity, resources);
            }
        }
    }

    private void PublishCompletedBackgroundRecordings()
    {
        while (_backgroundWorker.TryTakeCompleted(out var result))
        {
            using (result)
            {
                if (!_backgroundChunks.Remove(result.RequestId, out var chunk) ||
                    chunk.PendingRecordingId != result.RequestId)
                {
                    continue;
                }

                chunk.PendingRecordingId = 0;
                if (result.IsCancelled)
                    continue;
                if (result.IsFailed)
                {
                    chunk.BuildFailed = true;
                    continue;
                }

                chunk.CommandList = result.TakeCommandList();
                if (chunk.CommandList is null)
                {
                    chunk.BuildFailed = true;
                    continue;
                }

                chunk.RecordedEntityCount = result.RecordedEntityCount;
                chunk.EstimatedBytes = EstimateCommandListBytes(chunk);
                chunk.LastUsed = ++_usageStamp;
                _statistics.RecordBackgroundCommandListBuild(
                    result.ElapsedMilliseconds);
                TrimToBudget(chunk);
            }
        }
    }

    private void CancelBackgroundRecordings(bool waitForResources = true)
    {
        if (waitForResources)
            _backgroundWorker.CancelAndWait();
        else
            _backgroundWorker.CancelPending();
        foreach (var (requestId, chunk) in _backgroundChunks)
        {
            if (chunk.PendingRecordingId == requestId)
                chunk.PendingRecordingId = 0;
        }
        _backgroundChunks.Clear();
    }

    private static CadRenderOptions CreateBuildOptions(
        CadRenderOptions options,
        double viewportZoom,
        bool? enableGeometryRealizations = null) => new()
        {
            ActiveOwnerBlockId = options.ActiveOwnerBlockId,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsAntialiasingEnabled = options.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
            IsBackgroundChunkRecordingEnabled = options.IsBackgroundChunkRecordingEnabled,
            EnableGeometryRealizations =
                enableGeometryRealizations ?? options.EnableGeometryRealizations,
            TransformScaleMultiplier = viewportZoom,
            KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
            EntityLineWeightWorldScale = options.EntityLineWeightWorldScale,
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
        double padding)
    {
        if (renderBounds is null || chunkBounds.IsEmpty)
            return true;

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

    private static bool ContainsInlineSelectionDependency(
        RenderChunk chunk,
        CadHandleScene? handleScene)
    {
        if (handleScene is null || handleScene.SelectionReferenceCount == 0)
            return false;

        foreach (var entityId in chunk.DependencyEntityIds)
        {
            if (handleScene.TryGetSelectionReference(entityId, out _))
                return true;
        }

        return false;
    }

    private static bool AreAllTopLevelEntitiesHidden(
        RenderChunk chunk,
        IReadOnlySet<EntityId> hiddenEntityIds)
    {
        if (hiddenEntityIds.Count == 0 || chunk.Entities.Count == 0)
            return false;

        foreach (var entity in chunk.Entities)
        {
            if (!hiddenEntityIds.Contains(entity.Id))
                return false;
        }

        return true;
    }

    private static long EstimateCommandListBytes(RenderChunk chunk) =>
        4L * 1024 +
        chunk.RecordedEntityCount * 256L +
        chunk.DependencyEntityIds.Count * 32L;

    private void TrimToBudget(RenderChunk protectedChunk)
    {
        var overflow = EstimatedBytes - CacheBudgetBytes;
        if (overflow <= 0)
            return;

        try
        {
            foreach (var profile in _profiles.Values)
            foreach (var chunk in profile.Chunks)
            {
                if (!ReferenceEquals(chunk, protectedChunk) && chunk.CommandList is not null)
                    _evictionCandidates.Add(chunk, chunk.LastUsed);
            }

            while (overflow > 0 && _evictionCandidates.TryTake(out var chunk))
            {
                overflow -= chunk.EstimatedBytes;
                chunk.EvictForBudget();
                _statistics.RecordGpuCacheEviction();
            }
        }
        finally
        {
            _evictionCandidates.Clear();
        }
    }

    private void CollectDependentOwners(CadDocument document, BlockId ownerId)
    {
        var pending = new Stack<BlockId>();
        if (_invalidOwners.Add(ownerId))
            pending.Push(ownerId);
        while (pending.TryPop(out var owner))
        {
            foreach (var id in document.GetBlockReferenceIds(owner))
            {
                if (document.TryGetEntity(id, out var entity) && entity is { IsErased: false } &&
                    _invalidOwners.Add(entity.OwnerBlockId))
                    pending.Push(entity.OwnerBlockId);
            }
        }
    }

    private void EnsureDocument(CadDocument document)
    {
        if (ReferenceEquals(_document, document))
            return;
        ClearChunkCaches();
        _document = document;
    }

    private void TrimProfiles(RenderProfileKey currentKey)
    {
        if (_profiles.Count > MaximumProfiles)
            CancelBackgroundRecordings(waitForResources: false);
        while (_profiles.Count > MaximumProfiles)
        {
            RenderProfile? oldest = null;
            foreach (var pair in _profiles)
            {
                if (!pair.Key.Equals(currentKey) &&
                    (oldest is null || pair.Value.LastUsed < oldest.LastUsed))
                    oldest = pair.Value;
            }
            if (oldest is null)
                break;
            oldest.Dispose();
            _profiles.Remove(oldest.Key);
        }
    }

    private void ClearProfiles()
    {
        foreach (var profile in _profiles.Values)
            profile.Dispose();
        _profiles.Clear();
        _estimatedBytes = 0;
    }

    private void ClearChunkCaches()
    {
        CancelBackgroundRecordings();
        ClearProfiles();
        _chunkPlans.Clear();
        _planBuilders.Clear();
    }

    private void AdjustEstimatedBytes(long delta)
    {
        _estimatedBytes = Math.Max(0, _estimatedBytes + delta);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ClearChunkCaches();
        _backgroundWorker.Dispose();
        _backgroundFactory = null;
        _backgroundDevice = null;
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
        bool EnableGeometryRealizations,
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits,
        long EntityLineWeightWorldScaleBits)
    {
        public static RenderProfileKey Create(CadRenderOptions options, double zoom)
        {
            var quantizedZoom = Direct2DRenderScaleBucket.Quantize(zoom);
            return new RenderProfileKey(
                options.ActiveOwnerBlockId,
                BitConverter.DoubleToInt64Bits(quantizedZoom),
                options.IsAntialiasingEnabled,
                options.IsTextAntialiasingEnabled,
                options.EnableGeometryRealizations,
                options.IsLevelOfDetailEnabled,
                options.KeepStrokeWidthScreenConstant,
                BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth),
                BitConverter.DoubleToInt64Bits(options.EntityLineWeightWorldScale));
        }

    }

    private sealed record RenderChunkPlan(
        IReadOnlyList<CadEntity> Entities,
        IReadOnlyList<EntityId> DependencyEntityIds,
        bool IsCacheable);

    private sealed class RenderChunkPlanBuilder
    {
        private readonly Direct2DCommandListChunkCache _owner;
        private readonly CadDocument _document;
        private readonly IReadOnlyList<CadEntity> _orderedEntities;
        private readonly IReadOnlySet<EntityId>? _preparedChunkBreaks;
        private readonly List<RenderChunkPlan> _plans = [];
        private readonly List<CadEntity> _pending = new(EntitiesPerChunk);
        private readonly Dictionary<BlockId, bool> _blockCacheability = [];
        private readonly Dictionary<BlockId, IReadOnlyList<EntityId>> _blockDependencies = [];
        private readonly HashSet<BlockId> _visitingBlocks = [];
        private CadRectD _pendingBounds = CadRectD.Empty;
        private double _pendingFootprint;
        private int _nextEntityIndex;

        public IReadOnlyList<RenderChunkPlan> Plans => _plans;
        public bool IsComplete { get; private set; }

        public RenderChunkPlanBuilder(
            Direct2DCommandListChunkCache owner,
            CadDocument document,
            BlockId ownerBlockId,
            IReadOnlyList<CadEntity> orderedEntities)
        {
            _owner = owner;
            _document = document;
            _orderedEntities = orderedEntities;
            _preparedChunkBreaks = owner._entityOrderCache.GetAdaptiveChunkBreakEntityIds(
                document,
                ownerBlockId);
        }

        public bool BuildStep(double budgetMilliseconds)
        {
            if (IsComplete)
                return true;

            var started = Stopwatch.GetTimestamp();
            var processed = 0;
            while (_nextEntityIndex < _orderedEntities.Count)
            {
                Process(_orderedEntities[_nextEntityIndex++]);
                processed++;
                if (processed > 0 &&
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budgetMilliseconds)
                {
                    return false;
                }
            }

            FlushCacheable();
            IsComplete = true;
            return true;
        }

        private void Process(CadEntity entity)
        {
            _visitingBlocks.Clear();
            if (_owner.IsCacheable(
                    _document,
                    entity,
                    _blockCacheability,
                    _visitingBlocks))
            {
                if (_pending.Count > 0 && _preparedChunkBreaks?.Contains(entity.Id) == true)
                    FlushCacheable();
                if (Direct2DAdaptiveChunkPlanner.ShouldFlushBefore(
                        _pending.Count,
                        MinimumSpatialChunkEntityCount,
                        EntitiesPerChunk,
                        _pendingBounds,
                        _pendingFootprint,
                        entity.Bounds))
                {
                    FlushCacheable();
                }

                _pending.Add(entity);
                _pendingBounds = _pendingBounds.Union(entity.Bounds);
                _pendingFootprint += Direct2DAdaptiveChunkPlanner.EstimateFootprint(entity.Bounds);
                return;
            }

            FlushCacheable();
            _plans.Add(_owner.CreateChunkPlan(
                _document,
                [entity],
                isCacheable: false,
                _blockDependencies));
        }

        private void FlushCacheable()
        {
            if (_pending.Count == 0)
                return;

            _plans.Add(_owner.CreateChunkPlan(
                _document,
                _pending.ToArray(),
                isCacheable: true,
                _blockDependencies));
            _pending.Clear();
            _pendingBounds = CadRectD.Empty;
            _pendingFootprint = 0;
        }
    }

    private sealed class RenderProfile : IDisposable
    {
        private readonly Dictionary<EntityId, List<RenderChunk>> _chunksByDependency = [];

        public RenderProfileKey Key { get; }
        public IReadOnlyList<RenderChunk> Chunks { get; }
        public int EntityCount { get; }
        public bool HasPendingBuilds
        {
            get
            {
                foreach (var chunk in Chunks)
                {
                    if (chunk.IsCacheable &&
                        chunk.CommandList is null &&
                        !chunk.BuildFailed &&
                        !chunk.WasBudgetEvicted)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        public long LastUsed { get; set; }
        public RenderProfile(RenderProfileKey key, IReadOnlyList<RenderChunk> chunks)
        {
            Key = key;
            Chunks = chunks;
            foreach (var chunk in chunks)
            {
                EntityCount += chunk.Entities.Count;
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

        private readonly HashSet<RenderChunk> _invalidatedChunks = [];

        public int Invalidate(IReadOnlyList<CadEntityChange> changes, CadEntityChangeKind kinds)
        {
            try
            {
                foreach (var change in changes)
                {
                    if ((change.Kind & kinds) == 0 ||
                        !_chunksByDependency.TryGetValue(change.EntityId, out var chunks))
                        continue;
                    foreach (var chunk in chunks)
                    {
                        if (_invalidatedChunks.Add(chunk))
                            chunk.Invalidate();
                    }
                }
                return _invalidatedChunks.Count;
            }
            finally { _invalidatedChunks.Clear(); }
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
        private readonly Action<long> _estimatedBytesChanged;
        private long _estimatedBytes;

        public IReadOnlyList<CadEntity> Entities { get; }
        public IReadOnlyList<EntityId> DependencyEntityIds { get; }
        public CadRectD Bounds { get; private set; }
        public bool IsCacheable { get; }
        public ID2D1CommandList? CommandList { get; set; }
        public long PendingRecordingId { get; set; }
        public int RecordedEntityCount { get; set; }
        public bool BuildFailed { get; set; }
        public bool WasBudgetEvicted { get; private set; }
        public long EstimatedBytes
        {
            get => _estimatedBytes;
            set
            {
                var normalized = Math.Max(0, value);
                var delta = normalized - _estimatedBytes;
                _estimatedBytes = normalized;
                _estimatedBytesChanged(delta);
            }
        }
        public long LastUsed { get; set; }

        public RenderChunk(
            IReadOnlyList<CadEntity> entities,
            IReadOnlyList<EntityId> dependencyEntityIds,
            CadRectD bounds,
            bool isCacheable,
            Action<long> estimatedBytesChanged)
        {
            Entities = entities;
            DependencyEntityIds = dependencyEntityIds;
            Bounds = bounds;
            IsCacheable = isCacheable;
            _estimatedBytesChanged = estimatedBytesChanged;
        }

        public void Invalidate()
        {
            CommandList?.Dispose();
            CommandList = null;
            PendingRecordingId = 0;
            RecordedEntityCount = 0;
            BuildFailed = false;
            WasBudgetEvicted = false;
            EstimatedBytes = 0;
            Bounds = CadRectD.Empty;
            foreach (var entity in Entities)
                Bounds = Bounds.Union(entity.Bounds);
        }

        public void EvictForBudget()
        {
            CommandList?.Dispose();
            CommandList = null;
            PendingRecordingId = 0;
            RecordedEntityCount = 0;
            EstimatedBytes = 0;
            WasBudgetEvicted = true;
        }

        public void Dispose()
        {
            CommandList?.Dispose();
            CommandList = null;
            PendingRecordingId = 0;
            RecordedEntityCount = 0;
            EstimatedBytes = 0;
        }
    }
}
