using System.Diagnostics;
using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Handles;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Overlays;

internal delegate void Direct2DSelectionReferenceDrawCallback(
    ID2D1DeviceContext context,
    CadDocument document,
    CadViewport viewport,
    CadSelectionEntityReference reference,
    CadRectD? renderWorldBounds,
    CadRenderOptions options);

/// <summary>
/// Retains large, stable selection outlines as small command-list runs. Dynamic selection
/// content remains interleaved with cached runs so hatch and drag previews keep their
/// view-dependent behavior.
/// </summary>
internal sealed class Direct2DSelectionCommandListCache : IDisposable
{
    private const int MinimumSelectionCount = 512;
    private const int MinimumSpatialChunkReferenceCount = 48;
    private const int ReferencesPerChunk = 256;
    private const int MaximumProfiles = 3;
    private const double BuildBudgetMilliseconds = 4.0;
    private const double PlanBuildBudgetMilliseconds = 1.5;
    internal const long CacheBudgetBytes = 32L * 1024 * 1024;

    private readonly Direct2DResourceCache _resourceCache;
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private readonly Dictionary<SelectionProfileKey, SelectionProfile> _profiles = [];
    private IReadOnlyList<SelectionChunkPlan>? _chunkPlans;
    private SelectionChunkPlanBuilder? _planBuilder;
    private CadDocument? _document;
    private long _selectionVersion = long.MinValue;
    private long _usageStamp;
    private long _estimatedBytes;
    private bool _disposed;

    public Direct2DSelectionCommandListCache(
        Direct2DResourceCache resourceCache,
        Direct2DRenderStatisticsCollector statistics)
    {
        _resourceCache = resourceCache;
        _statistics = statistics;
    }

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

    public bool Prepare(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene? scene,
        CadRenderOptions options,
        Direct2DSelectionReferenceDrawCallback drawReference,
        bool buildStep)
    {
        ThrowIfDisposed();
        EnsureState(document, scene);
        if (!CanUse(scene, options))
            return false;

        var key = SelectionProfileKey.Create(options, viewport.Zoom);
        if (!buildStep && !_profiles.ContainsKey(key))
            return true;

        if (!_profiles.TryGetValue(key, out var profile))
        {
            if (_chunkPlans is null)
            {
                _planBuilder ??= new SelectionChunkPlanBuilder(
                    this,
                    document,
                    scene!.SelectionReferences);
                if (!_planBuilder.BuildStep(PlanBuildBudgetMilliseconds))
                    return true;
                _chunkPlans = _planBuilder.Plans;
                _planBuilder = null;
            }
            profile = BuildProfile(key, _chunkPlans);
            _profiles.Add(key, profile);
            TrimProfiles(key);
        }

        profile.LastUsed = ++_usageStamp;
        if (!profile.HasCacheableChunks)
            return false;
        if (!buildStep)
            return profile.HasPendingBuilds;

        var buildOptions = CreateBuildOptions(options, viewport.Zoom);
        var started = Stopwatch.GetTimestamp();
        var visibleWorldBounds = viewport.VisibleWorldBounds;
        var renderPadding = Direct2DSelectionRenderer.ResolveSelectionRenderPadding(
            scene!,
            viewport.Zoom);
        for (var pass = 0; pass < 2; pass++)
        {
            var buildVisibleChunks = pass == 0;
            foreach (var chunk in profile.Chunks)
            {
                if (!chunk.IsCacheable ||
                    chunk.CommandList is not null ||
                    chunk.BuildFailed ||
                    chunk.WasBudgetEvicted ||
                    IntersectsRenderBounds(chunk.Bounds, visibleWorldBounds, renderPadding) !=
                    buildVisibleChunks)
                {
                    continue;
                }

                chunk.CommandList = RecordChunk(
                    context,
                    document,
                    viewport,
                    buildOptions,
                    chunk,
                    drawReference);
                if (chunk.CommandList is not null)
                {
                    chunk.EstimatedBytes = EstimateCommandListBytes(chunk);
                    chunk.LastUsed = ++_usageStamp;
                    _statistics.RecordSelectionCommandListBuild();
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
        CadHandleScene scene,
        CadRenderOptions options,
        Direct2DSelectionReferenceDrawCallback drawReference,
        bool requireCompleteCache = false)
    {
        ThrowIfDisposed();
        EnsureState(document, scene);
        if (!CanUse(scene, options) ||
            !_profiles.TryGetValue(SelectionProfileKey.Create(options, viewport.Zoom), out var profile) ||
            !profile.HasCacheableChunks)
        {
            return false;
        }

        profile.LastUsed = ++_usageStamp;
        var renderBounds = options.DirtyWorldBounds is { IsEmpty: false } dirty
            ? dirty
            : viewport.VisibleWorldBounds;
        var renderPadding = Direct2DSelectionRenderer.ResolveSelectionRenderPadding(
            scene,
            viewport.Zoom);
        if (requireCompleteCache &&
            profile.Chunks.Any(chunk =>
                chunk.IsCacheable &&
                chunk.CommandList is null &&
                IntersectsRenderBounds(
                    chunk.Bounds,
                    renderBounds,
                    renderPadding)))
        {
            return false;
        }

        foreach (var chunk in profile.Chunks)
        {
            if (!IntersectsRenderBounds(chunk.Bounds, renderBounds, renderPadding))
                continue;

            if (chunk.CommandList is not null)
            {
                context.DrawImage(
                    chunk.CommandList,
                    null,
                    null,
                    InterpolationMode.Linear,
                    CompositeMode.SourceOver);
                _statistics.RecordSelectionCommandListReplay();
                _statistics.RecordSelectionEntities(chunk.RecordedReferenceCount);
                chunk.LastUsed = ++_usageStamp;
                continue;
            }

            foreach (var reference in chunk.References)
                drawReference(
                    context,
                    document,
                    viewport,
                    reference,
                    renderBounds,
                    options);
        }

        return true;
    }

    public void ApplyChanges(CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(changes);
        if (!changes.DocumentChanged)
            return;

        if (changes.AffectsDocumentStructure ||
            changes.AffectsViewSettings ||
            changes.EntityChanges.Any(static change =>
                (change.Kind & (CadEntityChangeKind.Fill |
                                CadEntityChangeKind.Geometry |
                                CadEntityChangeKind.Deleted |
                                CadEntityChangeKind.Rotation)) != 0))
        {
            ClearSelectionCaches();
            return;
        }

        const CadEntityChangeKind visualChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.Layer |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.EmbeddedData |
            CadEntityChangeKind.Opacity |
            CadEntityChangeKind.Rotation;
        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & visualChanges) == 0)
                continue;
            foreach (var profile in _profiles.Values)
                profile.Invalidate(change.EntityId);
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        ClearSelectionCaches();
        _document = null;
        _selectionVersion = long.MinValue;
    }

    private SelectionProfile BuildProfile(
        SelectionProfileKey key,
        IReadOnlyList<SelectionChunkPlan> plans)
    {
        var chunks = new SelectionChunk[plans.Count];
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            chunks[index] = new SelectionChunk(
                plan.References,
                plan.DependencyEntityIds,
                plan.Bounds,
                plan.IsCacheable,
                AdjustEstimatedBytes);
        }

        return new SelectionProfile(key, chunks);
    }

    private bool IsCacheable(
        CadDocument document,
        EntityId entityId,
        Dictionary<BlockId, bool> blockCacheability,
        HashSet<BlockId> visitingBlocks)
    {
        if (!document.TryGetEntity(entityId, out var entity) || entity is null)
            return false;

        if (entity is not CadBlockReference reference)
        {
            return _resourceCache.TryGetEntityResources(entity.Id, out var resources) &&
                   resources is { HatchBrush: null };
        }

        if (blockCacheability.TryGetValue(reference.DefinitionBlockId, out var cached))
            return cached;
        if (!visitingBlocks.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return false;
        }

        var cacheable = true;
        try
        {
            foreach (var child in document.GetEntitiesInBlock(definition.Id))
            {
                if (child.IsErased || !child.IsVisible)
                    continue;
                if (!IsCacheable(
                        document,
                        child.Id,
                        blockCacheability,
                        visitingBlocks))
                {
                    cacheable = false;
                    break;
                }
            }
        }
        finally
        {
            visitingBlocks.Remove(reference.DefinitionBlockId);
        }

        blockCacheability[reference.DefinitionBlockId] = cacheable;
        return cacheable;
    }

    private ID2D1CommandList? RecordChunk(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        SelectionChunk chunk,
        Direct2DSelectionReferenceDrawCallback drawReference)
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
            foreach (var reference in chunk.References)
            {
                drawReference(
                    context,
                    document,
                    viewport,
                    reference,
                    renderWorldBounds: null,
                    options);
                recordedCount++;
            }

            var result = context.EndDraw();
            isDrawing = false;
            if (result.Failure)
                return null;

            context.Target = previousTarget;
            commandList.Close();
            chunk.RecordedReferenceCount = recordedCount;
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

    private SelectionChunkPlan CreateChunkPlan(
        CadDocument document,
        IReadOnlyList<CadSelectionEntityReference> references,
        bool isCacheable,
        Dictionary<BlockId, IReadOnlyList<EntityId>> blockDependencies)
    {
        var bounds = CadRectD.Empty;
        var dependencies = new HashSet<EntityId>();
        foreach (var reference in references)
        {
            bounds = bounds.Union(reference.EntityBounds.Translate(reference.Offset));
            dependencies.Add(reference.EntityId);
            if (!document.TryGetEntity(reference.EntityId, out var entity) ||
                entity is not CadBlockReference blockReference)
            {
                continue;
            }

            foreach (var dependency in ResolveBlockDependencies(
                         document,
                         blockReference.DefinitionBlockId,
                         blockDependencies,
                         []))
            {
                dependencies.Add(dependency);
            }
        }

        return new SelectionChunkPlan(
            references,
            dependencies.ToArray(),
            bounds,
            isCacheable);
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
        foreach (var child in document.GetEntitiesInBlock(blockId))
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

    private static CadRenderOptions CreateBuildOptions(
        CadRenderOptions source,
        double viewportZoom) => new()
        {
            ActiveOwnerBlockId = source.ActiveOwnerBlockId,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsAntialiasingEnabled = source.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = source.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = source.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = viewportZoom,
            KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
            HiddenEntityIds = CadRenderOptions.NoHiddenEntities
        };

    private static bool CanUse(CadHandleScene? scene, CadRenderOptions options)
    {
        return scene is
        {
            SelectionReferenceCount: >= MinimumSelectionCount,
            HasTranslatedSelectionReferences: false
        } &&
               options.ActiveLayoutId is null &&
               options.HiddenEntityIds.Count == 0;
    }

    private static bool IntersectsRenderBounds(
        CadRectD chunkBounds,
        CadRectD renderBounds,
        double padding)
    {
        if (chunkBounds.IsEmpty || renderBounds.IsEmpty)
            return true;

        var paintBounds = chunkBounds.Inflate(padding);
        return paintBounds.Intersects(renderBounds) ||
               paintBounds.Contains(renderBounds.Center) ||
               renderBounds.Contains(paintBounds);
    }

    private static long EstimateCommandListBytes(SelectionChunk chunk) =>
        4L * 1024 +
        chunk.RecordedReferenceCount * 192L +
        chunk.DependencyEntityIds.Count * 32L;

    private void TrimToBudget(SelectionChunk protectedChunk)
    {
        var overflow = EstimatedBytes - CacheBudgetBytes;
        if (overflow <= 0)
            return;

        foreach (var chunk in _profiles.Values
                     .SelectMany(static profile => profile.Chunks)
                     .Where(chunk =>
                         !ReferenceEquals(chunk, protectedChunk) &&
                         chunk.CommandList is not null)
                     .OrderBy(static chunk => chunk.LastUsed)
                     .ToArray())
        {
            if (overflow <= 0)
                break;
            overflow -= chunk.EstimatedBytes;
            chunk.EvictForBudget();
            _statistics.RecordGpuCacheEviction();
        }
    }

    private void EnsureState(CadDocument document, CadHandleScene? scene)
    {
        var selectionVersion = scene?.SelectionVersion ?? long.MinValue;
        if (ReferenceEquals(_document, document) &&
            _selectionVersion == selectionVersion)
        {
            return;
        }

        ClearSelectionCaches();
        _document = document;
        _selectionVersion = selectionVersion;
    }

    private void TrimProfiles(SelectionProfileKey currentKey)
    {
        while (_profiles.Count > MaximumProfiles)
        {
            var oldest = _profiles
                .Where(pair => !pair.Key.Equals(currentKey))
                .MinBy(static pair => pair.Value.LastUsed);
            oldest.Value.Dispose();
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

    private void ClearSelectionCaches()
    {
        ClearProfiles();
        _chunkPlans = null;
        _planBuilder = null;
    }

    private void AdjustEstimatedBytes(long delta)
    {
        _estimatedBytes = Math.Max(0, _estimatedBytes + delta);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ClearSelectionCaches();
        _document = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DSelectionCommandListCache));
    }

    private readonly record struct SelectionProfileKey(
        BlockId OwnerBlockId,
        long ZoomBits,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits)
    {
        public static SelectionProfileKey Create(CadRenderOptions options, double zoom)
        {
            var quantizedZoom = Direct2DRenderScaleBucket.Quantize(zoom);
            return new SelectionProfileKey(
                options.ActiveOwnerBlockId,
                BitConverter.DoubleToInt64Bits(quantizedZoom),
                options.IsAntialiasingEnabled,
                options.IsTextAntialiasingEnabled,
                options.IsLevelOfDetailEnabled,
                options.KeepStrokeWidthScreenConstant,
                BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth));
        }

    }

    private sealed record SelectionChunkPlan(
        IReadOnlyList<CadSelectionEntityReference> References,
        IReadOnlyList<EntityId> DependencyEntityIds,
        CadRectD Bounds,
        bool IsCacheable);

    private sealed class SelectionChunkPlanBuilder
    {
        private readonly Direct2DSelectionCommandListCache _owner;
        private readonly CadDocument _document;
        private readonly IReadOnlyList<CadSelectionEntityReference> _references;
        private readonly List<SelectionChunkPlan> _plans = [];
        private readonly List<CadSelectionEntityReference> _pending = new(ReferencesPerChunk);
        private readonly Dictionary<BlockId, bool> _blockCacheability = [];
        private readonly Dictionary<BlockId, IReadOnlyList<EntityId>> _blockDependencies = [];
        private readonly HashSet<BlockId> _visitingBlocks = [];
        private CadRectD _pendingBounds = CadRectD.Empty;
        private double _pendingFootprint;
        private int _nextReferenceIndex;

        public IReadOnlyList<SelectionChunkPlan> Plans => _plans;
        public bool IsComplete { get; private set; }

        public SelectionChunkPlanBuilder(
            Direct2DSelectionCommandListCache owner,
            CadDocument document,
            IReadOnlyList<CadSelectionEntityReference> references)
        {
            _owner = owner;
            _document = document;
            _references = references;
        }

        public bool BuildStep(double budgetMilliseconds)
        {
            if (IsComplete)
                return true;

            var started = Stopwatch.GetTimestamp();
            var processed = 0;
            while (_nextReferenceIndex < _references.Count)
            {
                Process(_references[_nextReferenceIndex++]);
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

        private void Process(CadSelectionEntityReference reference)
        {
            _visitingBlocks.Clear();
            if (_owner.IsCacheable(
                    _document,
                    reference.EntityId,
                    _blockCacheability,
                    _visitingBlocks))
            {
                var referenceBounds = reference.EntityBounds.Translate(reference.Offset);
                if (Direct2DAdaptiveChunkPlanner.ShouldFlushBefore(
                        _pending.Count,
                        MinimumSpatialChunkReferenceCount,
                        ReferencesPerChunk,
                        _pendingBounds,
                        _pendingFootprint,
                        referenceBounds))
                {
                    FlushCacheable();
                }

                _pending.Add(reference);
                _pendingBounds = _pendingBounds.Union(referenceBounds);
                _pendingFootprint += Direct2DAdaptiveChunkPlanner.EstimateFootprint(referenceBounds);
                return;
            }

            FlushCacheable();
            _plans.Add(_owner.CreateChunkPlan(
                _document,
                [reference],
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

    private sealed class SelectionProfile : IDisposable
    {
        private readonly Dictionary<EntityId, List<SelectionChunk>> _chunksByDependency = [];

        public SelectionProfileKey Key { get; }
        public IReadOnlyList<SelectionChunk> Chunks { get; }
        public bool HasCacheableChunks { get; }
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
        public SelectionProfile(
            SelectionProfileKey key,
            IReadOnlyList<SelectionChunk> chunks)
        {
            Key = key;
            Chunks = chunks;
            foreach (var chunk in chunks)
            {
                HasCacheableChunks |= chunk.IsCacheable;
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
        }
    }

    private sealed class SelectionChunk(
        IReadOnlyList<CadSelectionEntityReference> references,
        IReadOnlyList<EntityId> dependencyEntityIds,
        CadRectD bounds,
        bool isCacheable,
        Action<long> estimatedBytesChanged) : IDisposable
    {
        private long _estimatedBytes;

        public IReadOnlyList<CadSelectionEntityReference> References { get; } = references;
        public IReadOnlyList<EntityId> DependencyEntityIds { get; } = dependencyEntityIds;
        public CadRectD Bounds { get; } = bounds;
        public bool IsCacheable { get; } = isCacheable;
        public ID2D1CommandList? CommandList { get; set; }
        public int RecordedReferenceCount { get; set; }
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
                estimatedBytesChanged(delta);
            }
        }
        public long LastUsed { get; set; }

        public void Invalidate()
        {
            CommandList?.Dispose();
            CommandList = null;
            RecordedReferenceCount = 0;
            BuildFailed = false;
            WasBudgetEvicted = false;
            EstimatedBytes = 0;
        }

        public void EvictForBudget()
        {
            CommandList?.Dispose();
            CommandList = null;
            RecordedReferenceCount = 0;
            EstimatedBytes = 0;
            WasBudgetEvicted = true;
        }

        public void Dispose()
        {
            Invalidate();
        }
    }
}
