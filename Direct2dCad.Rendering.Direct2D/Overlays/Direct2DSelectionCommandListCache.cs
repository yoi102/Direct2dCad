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
    private const int ReferencesPerChunk = 256;
    private const int MaximumProfiles = 3;
    private const double BuildBudgetMilliseconds = 4.0;

    private readonly Direct2DResourceCache _resourceCache;
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private readonly Dictionary<SelectionProfileKey, SelectionProfile> _profiles = [];
    private CadDocument? _document;
    private long _selectionVersion = long.MinValue;
    private long _usageStamp;
    private bool _disposed;

    public Direct2DSelectionCommandListCache(
        Direct2DResourceCache resourceCache,
        Direct2DRenderStatisticsCollector statistics)
    {
        _resourceCache = resourceCache;
        _statistics = statistics;
    }

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
            profile = BuildProfile(document, key, scene!.SelectionReferences);
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
                drawReference);
            if (chunk.CommandList is not null)
                _statistics.RecordSelectionCommandListBuild();
            else
                chunk.BuildFailed = true;

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= BuildBudgetMilliseconds)
                break;
        }

        return profile.HasPendingBuilds;
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene scene,
        CadRenderOptions options,
        Direct2DSelectionReferenceDrawCallback drawReference)
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
        foreach (var chunk in profile.Chunks)
        {
            if (!IntersectsRenderBounds(chunk.Bounds, renderBounds, viewport.Zoom))
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
                (change.Kind & CadEntityChangeKind.Fill) != 0))
        {
            ClearProfiles();
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
        ClearProfiles();
        _document = null;
        _selectionVersion = long.MinValue;
    }

    private SelectionProfile BuildProfile(
        CadDocument document,
        SelectionProfileKey key,
        IReadOnlyList<CadSelectionEntityReference> references)
    {
        var chunks = new List<SelectionChunk>();
        var pending = new List<CadSelectionEntityReference>(ReferencesPerChunk);
        var blockCacheability = new Dictionary<BlockId, bool>();
        var blockDependencies = new Dictionary<BlockId, IReadOnlyList<EntityId>>();

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

        foreach (var reference in references)
        {
            if (IsCacheable(document, reference.EntityId, blockCacheability, []))
            {
                pending.Add(reference);
                if (pending.Count >= ReferencesPerChunk)
                    FlushCacheable();
                continue;
            }

            FlushCacheable();
            chunks.Add(CreateChunk(
                document,
                [reference],
                isCacheable: false,
                blockDependencies));
        }

        FlushCacheable();
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

    private SelectionChunk CreateChunk(
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

        return new SelectionChunk(
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
        double zoom)
    {
        if (chunkBounds.IsEmpty || renderBounds.IsEmpty)
            return true;

        var padding = 64.0 / Math.Max(zoom, double.Epsilon);
        var paintBounds = chunkBounds.Inflate(padding);
        return paintBounds.Intersects(renderBounds) ||
               paintBounds.Contains(renderBounds.Center) ||
               renderBounds.Contains(paintBounds);
    }

    private void EnsureState(CadDocument document, CadHandleScene? scene)
    {
        var selectionVersion = scene?.SelectionVersion ?? long.MinValue;
        if (ReferenceEquals(_document, document) &&
            _selectionVersion == selectionVersion)
        {
            return;
        }

        ClearProfiles();
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

    private sealed class SelectionProfile : IDisposable
    {
        private readonly Dictionary<EntityId, List<SelectionChunk>> _chunksByDependency = [];

        public SelectionProfileKey Key { get; }
        public IReadOnlyList<SelectionChunk> Chunks { get; }
        public bool HasCacheableChunks { get; }
        public bool HasPendingBuilds => Chunks.Any(static chunk =>
            chunk.IsCacheable && chunk.CommandList is null && !chunk.BuildFailed);
        public long LastUsed { get; set; }

        public SelectionProfile(
            SelectionProfileKey key,
            IReadOnlyList<SelectionChunk> chunks)
        {
            Key = key;
            Chunks = chunks;
            HasCacheableChunks = chunks.Any(static chunk => chunk.IsCacheable);
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
        }
    }

    private sealed class SelectionChunk(
        IReadOnlyList<CadSelectionEntityReference> references,
        IReadOnlyList<EntityId> dependencyEntityIds,
        CadRectD bounds,
        bool isCacheable) : IDisposable
    {
        public IReadOnlyList<CadSelectionEntityReference> References { get; } = references;
        public IReadOnlyList<EntityId> DependencyEntityIds { get; } = dependencyEntityIds;
        public CadRectD Bounds { get; } = bounds;
        public bool IsCacheable { get; } = isCacheable;
        public ID2D1CommandList? CommandList { get; set; }
        public int RecordedReferenceCount { get; set; }
        public bool BuildFailed { get; set; }

        public void Invalidate()
        {
            CommandList?.Dispose();
            CommandList = null;
            RecordedReferenceCount = 0;
            BuildFailed = false;
        }

        public void Dispose()
        {
            Invalidate();
        }
    }
}
