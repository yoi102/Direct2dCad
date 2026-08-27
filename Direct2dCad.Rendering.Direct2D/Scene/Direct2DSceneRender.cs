using System.Diagnostics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Overlays;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Transient;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Scene;

public sealed class Direct2DSceneRender : CadRender, ICadGeometryResourceManager, IDisposable
{
    private const int MaximumPreparedLayoutViewports = 4;
    private readonly Direct2DStyleResourceCache _styleResources = new();
    private readonly Direct2DTextFormatResourceCache _textFormatResources = new();
    private readonly Direct2DResourceCache _resourceCache;
    private readonly Direct2DBackgroundRenderer _backgroundRenderer;
    private readonly Direct2DTransientSceneRenderer _transientSceneRenderer;
    private readonly Direct2DSelectionRenderer _selectionRenderer;
    private readonly Direct2DEntityRenderer _entityRenderer;
    private readonly Direct2DOleRenderer _oleRenderer;
    private readonly Direct2DEntityReferenceRenderer _entityReferenceRenderer;
    private readonly Direct2DBlockReferenceRenderer _blockReferenceRenderer;
    private readonly Direct2DEntityOrderCache _entityOrderCache = new();
    private readonly Direct2DRenderStatisticsCollector _statistics = new();
    private readonly Direct2DMicroEntityAggregator _microEntityAggregator = new();
    private readonly Direct2DCommandListChunkCache _commandListCache;
    private readonly Direct2DSceneTileCache _tileCache;
    private readonly List<EntityId> _visibleEntityIds = new(256);
    private readonly HashSet<EntityId> _visibleEntityIdSet = [];
    private readonly List<int> _visiblePacketIndices = new(256);
    private readonly List<CadEntity> _parallelVisibleEntities = new(256);
    private readonly Dictionary<EntityId, InlineMovePreview> _inlineMovePreviews = [];
    private bool _disposed;

    public CadRenderStatistics RenderStatistics { get; private set; } = CadRenderStatistics.Empty;

    public Direct2DSceneRender()
    {
        _resourceCache = new Direct2DResourceCache(
            _styleResources,
            _textFormatResources,
            _statistics);
        var geometryFactory = new Direct2DGeometryFactory();
        var transientRenderer = new Direct2DTransientRenderer(
            _resourceCache,
            geometryFactory,
            _styleResources,
            _textFormatResources,
            _statistics);
        var handleRenderer = new Direct2DHandleRenderer(_styleResources);

        _backgroundRenderer = new Direct2DBackgroundRenderer(_styleResources);
        _transientSceneRenderer = new Direct2DTransientSceneRenderer(
            transientRenderer,
            new Direct2DTransientImageCache(),
            new Direct2DTransientGroupCommandListCache(_resourceCache));
        _entityRenderer = new Direct2DEntityRenderer(
            _resourceCache,
            geometryFactory,
            _styleResources,
            _statistics);
        _oleRenderer = new Direct2DOleRenderer(
            _resourceCache,
            _entityOrderCache,
            _styleResources,
            _statistics);
        _selectionRenderer = new Direct2DSelectionRenderer(
            _resourceCache,
            _entityRenderer,
            _oleRenderer,
            transientRenderer,
            _styleResources,
            handleRenderer,
            _entityOrderCache,
            _statistics);
        _entityReferenceRenderer = new Direct2DEntityReferenceRenderer(
            _resourceCache,
            _entityRenderer,
            transientRenderer,
            _oleRenderer);
        _blockReferenceRenderer = new Direct2DBlockReferenceRenderer(
            _resourceCache,
            _entityRenderer,
            _oleRenderer,
            _styleResources,
            _entityOrderCache,
            _statistics);
        _commandListCache = new Direct2DCommandListChunkCache(
            _resourceCache,
            _entityOrderCache,
            _statistics);
        _tileCache = new Direct2DSceneTileCache(
            _resourceCache,
            _statistics);
    }

    public Direct2DOleDrawCallback? OleDrawCallback
    {
        get => _oleRenderer.DrawCallback;
        set => _oleRenderer.DrawCallback = value;
    }

    public Direct2DOleReleaseCallback? OleReleaseCallback
    {
        get => _oleRenderer.ReleaseCallback;
        set => _oleRenderer.ReleaseCallback = value;
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);
        ThrowIfDisposed();
        var hadBackgroundGeometryPreparation =
            _resourceCache.CancelBackgroundGeometryPreparation();
        _tileCache.ApplyChanges(document, changes);
        _commandListCache.ApplyChanges(document, changes);
        _blockReferenceRenderer.ApplyChanges(document, changes);
        _transientSceneRenderer.ApplyChanges(changes);
        _entityOrderCache.ApplyChanges(document, changes);
        _resourceCache.ApplyChanges(document, changes);
        _oleRenderer.ApplyChanges(document, changes);
        if (hadBackgroundGeometryPreparation)
            _resourceCache.ScheduleBackgroundGeometryPreparation(document);
    }

    public void ResetDeviceResources(
        ID2D1Factory? factory,
        IDWriteFactory? writeFactory,
        ID2D1Device? device,
        ID2D1DeviceContext? deviceContext,
        CadDocument? document = null,
        bool prepareBackgroundResources = true)
    {
        ThrowIfDisposed();
        _tileCache.Clear();
        _commandListCache.Clear();
        _backgroundRenderer.Clear();
        _blockReferenceRenderer.ClearCache();
        _entityOrderCache.Invalidate();
        _transientSceneRenderer.Clear();
        _oleRenderer.Clear();
        // Release entity leases before the device-bound shared caches are reset.
        _resourceCache.ClearCache();
        _styleResources.Reset(factory, deviceContext);
        _textFormatResources.Reset(writeFactory);
        _resourceCache.ResetDeviceResources(
            factory,
            writeFactory,
            deviceContext,
            prepareBackgroundResources ? document : null);
        _commandListCache.ResetBackgroundResources(factory, device);

        if (!prepareBackgroundResources && document is not null)
            _resourceCache.RebuildAll(document);
    }

    internal void SuspendBackgroundChunkRecording()
    {
        ThrowIfDisposed();
        _commandListCache.ResetBackgroundResources(factory: null, device: null);
    }

    public void RebuildAll(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _tileCache.Clear();
        _commandListCache.Clear();
        _blockReferenceRenderer.ClearCache();
        _entityOrderCache.Invalidate();
        _resourceCache.RebuildAll(document);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        var hadBackgroundGeometryPreparation =
            _resourceCache.CancelBackgroundGeometryPreparation();
        _tileCache.InvalidateEntity(document, entityId);
        _commandListCache.InvalidateEntity(entityId);
        _blockReferenceRenderer.ClearCache();
        _entityOrderCache.Invalidate();
        _resourceCache.RebuildEntityResources(document, entityId);
        if (hadBackgroundGeometryPreparation)
            _resourceCache.ScheduleBackgroundGeometryPreparation(document);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        _tileCache.RemoveEntity(entityId);
        _commandListCache.Clear();
        _blockReferenceRenderer.ClearCache();
        _entityOrderCache.Invalidate();
        _resourceCache.RemoveEntity(entityId);
        _oleRenderer.RemoveEntity(entityId);
    }

    public void InvalidateOleBitmap(EntityId entityId)
    {
        ThrowIfDisposed();
        _tileCache.InvalidateEntity(entityId);
        _oleRenderer.RemoveEntity(entityId);
    }

    public void BeginFrame(
        bool isFullFrame = true,
        int dirtyRegionCount = 1,
        double dirtyPlanningMilliseconds = 0)
    {
        ThrowIfDisposed();
        _statistics.BeginFrame(
            isFullFrame,
            dirtyRegionCount,
            dirtyPlanningMilliseconds);
        _resourceCache.BeginFrame();
        _styleResources.BeginFrame();
        _textFormatResources.BeginFrame();
    }

    internal void RecordCachePreparation(double milliseconds) =>
        _statistics.RecordCachePreparation(milliseconds);

    internal void RecordOlePreparation(double milliseconds) =>
        _statistics.RecordOlePreparation(milliseconds);

    internal void RecordSurfaceDraw(double milliseconds) =>
        _statistics.RecordSurfaceDraw(milliseconds);

    internal void RecordParallelFrame(
        CadParallelRenderingMode mode,
        int workerCount,
        int entityCount,
        double milliseconds,
        IReadOnlyList<CadRenderStatistics> workerStatistics) =>
        _statistics.RecordParallelFrame(
            mode,
            workerCount,
            entityCount,
            milliseconds,
            workerStatistics);

    public void CompleteFrame()
    {
        ThrowIfDisposed();
        try
        {
            try
            {
                _oleRenderer.CompleteFrame();
            }
            finally
            {
                try
                {
                    _styleResources.CompleteFrame();
                }
                finally
                {
                    _textFormatResources.CompleteFrame();
                }
            }
        }
        finally
        {
            _statistics.RecordGpuCacheEviction(
                _resourceCache.EnforceGeometryRealizationBudget());
            _statistics.RecordGeometryRealizations(
                _resourceCache.CaptureGeometryRealizationStatistics());
            _statistics.SetGpuCacheMemory(
                _tileCache.EstimatedBytes,
                _commandListCache.EstimatedBytes,
                selectionCommandListBytes: 0,
                _blockReferenceRenderer.EstimatedCacheBytes,
                _resourceCache.GeometryRealizationEstimatedBytes,
                _resourceCache.HatchTileEstimatedBytes,
                _resourceCache.ImageBitmapEstimatedBytes,
                _oleRenderer.EstimatedCacheBytes,
                Direct2DSceneTileCache.CacheBudgetBytes +
                Direct2DCommandListChunkCache.CacheBudgetBytes +
                Direct2DBlockReferenceRenderer.CacheBudgetBytes +
                Direct2DResourceCache.GeometryRealizationCacheBudgetBytes +
                Direct2DResourceCache.HatchTileCacheBudgetBytes +
                Direct2DOleRenderer.CacheBudgetBytes);
            RenderStatistics = _statistics.Snapshot();
            _statistics.EndFrame();
        }
    }

    public void PrepareOleTiles(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();
        options ??= new CadRenderOptions();
        if (options.ActiveLayoutId is not { } layoutId ||
            !document.TryGetLayout(layoutId, out var layout) ||
            layout is null)
        {
            _oleRenderer.PrepareTiles(document, viewport, transientScene, options);
            return;
        }

        _oleRenderer.PrepareTiles(
            document,
            viewport,
            options.ActiveLayoutViewportId is null ? transientScene : null,
            CreatePaperSpaceOptions(layout, options));
        if (_resourceCache.DeviceContext is not { } context)
            return;

        var paperTransform = CreateViewportTransform(viewport);
        foreach (var layoutViewport in layout.Viewports)
        {
            if (!layoutViewport.IsVisible)
                continue;

            var modelViewport = CreateModelViewport(viewport, layoutViewport);
            var modelOptions = CreateModelViewportOptions(options);
            var modelToScreen = CreateModelToPaperTransform(layoutViewport) * paperTransform;
            var orderedOleEntities = _entityOrderCache.GetOrderedOleEntities(
                document,
                modelOptions.ActiveOwnerBlockId);
            if (orderedOleEntities.Count > 0)
            {
                foreach (var visible in Direct2DEntityVisibility.Enumerate(
                             document,
                             modelViewport,
                             modelOptions,
                             _resourceCache,
                             orderedOleEntities,
                             _entityOrderCache))
                {
                    if (visible.Entity is not CadOleObject ole)
                        continue;
                    _oleRenderer.PrepareEntityTiles(
                        context,
                        ole,
                        viewport,
                        modelToScreen,
                        modelOptions);
                }
            }

            if (options.ActiveLayoutViewportId == layoutViewport.Id && transientScene is not null)
                _oleRenderer.PrepareTransientSceneTiles(
                    context,
                    document,
                    transientScene,
                    viewport,
                    modelToScreen,
                    modelOptions);
        }
    }

    public bool PrepareRenderCaches(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions? options = null,
        bool buildStep = true,
        CadHandleScene? handleScene = null,
        CadTransientScene? transientScene = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();
        if (_resourceCache.DeviceContext is not { } context)
            return false;

        options ??= new CadRenderOptions();
        var backgroundGeometryBuildPending =
            _resourceCache.ApplyBackgroundGeometryPreparation(
                document,
                buildStep ? 64 : 4);
        if (backgroundGeometryBuildPending)
            return true;

        if (buildStep)
            _resourceCache.BeginGeometryRealizationBuildBatch();
        if (options.ActiveLayoutId is { } layoutId &&
            document.TryGetLayout(layoutId, out var activeLayout) &&
            activeLayout is not null)
        {
            var layoutBuildPending = PrepareLayoutViewportCaches(
                context,
                document,
                viewport,
                activeLayout,
                handleScene,
                transientScene,
                options,
                buildStep);
            ScheduleBackgroundPreparationAfterCacheBuild(
                document,
                buildStep,
                layoutBuildPending);
            return layoutBuildPending;
        }

        var orderedEntities = _entityOrderCache.GetOrderedEntities(
            document,
            options.ActiveOwnerBlockId);
        var estimatedRenderWork = _entityOrderCache.GetEstimatedRenderWork(
            document,
            options.ActiveOwnerBlockId);
        var canBuildSceneTiles = handleScene is not { SelectionReferenceCount: > 0 };

        if (!buildStep)
        {
            var transientBuildPending = PrepareTransientCache(
                context,
                document,
                viewport,
                transientScene,
                options,
                buildStep: false);
            var blockDefinitionBuildPending = _blockReferenceRenderer.PrepareCache(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                buildStep: false);
            var commandListBuildPending = _commandListCache.Prepare(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: false);
            var tileBuildPending = canBuildSceneTiles && _tileCache.Prepare(
                context,
                document,
                viewport,
                options,
                estimatedRenderWork,
                DrawRetainedScene,
                buildStep: false);
            return transientBuildPending ||
                   blockDefinitionBuildPending ||
                   commandListBuildPending ||
                   tileBuildPending;
        }

        if (PrepareTransientCache(
                context,
                document,
                viewport,
                transientScene,
                options,
                buildStep: false))
        {
            PrepareTransientCache(
                context,
                document,
                viewport,
                transientScene,
                options,
                buildStep: true);
            return true;
        }

        if (_blockReferenceRenderer.PrepareCache(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                buildStep: false))
        {
            _blockReferenceRenderer.PrepareCache(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                buildStep: true);
            return true;
        }

        if (_commandListCache.Prepare(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: false))
        {
            _commandListCache.Prepare(
                context,
                document,
                viewport,
                options,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: true);
            return true;
        }

        if (!canBuildSceneTiles ||
            !_tileCache.Prepare(
                context,
                document,
                viewport,
                options,
                estimatedRenderWork,
                DrawRetainedScene,
                buildStep: false))
        {
            ScheduleBackgroundPreparationAfterCacheBuild(
                document,
                buildStep,
                cacheBuildPending: false);
            return false;
        }

        _tileCache.Prepare(
            context,
            document,
            viewport,
            options,
            estimatedRenderWork,
            DrawRetainedScene,
            buildStep: true);
        return true;
    }

    private void ScheduleBackgroundPreparationAfterCacheBuild(
        CadDocument document,
        bool buildStep,
        bool cacheBuildPending)
    {
        if (buildStep && !cacheBuildPending)
            _entityOrderCache.ScheduleBackgroundPreparation(document);
    }

    public override void Render(CadDocument document, CadViewport viewport, CadRenderOptions? options = null)
    {
        Render(document, viewport, null, null, options, Direct2DScenePasses.All);
    }

    public void Render(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene = null,
        CadRenderOptions? options = null)
    {
        Render(
            document,
            viewport,
            transientScene,
            handleScene,
            options,
            Direct2DScenePasses.All);
    }

    internal void RenderBase(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        Render(
            document,
            viewport,
            transientScene,
            handleScene,
            options,
            Direct2DScenePasses.Base);
    }

    internal IReadOnlyList<CadEntity> GetVisibleEntitiesForParallelRendering(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();

        var orderedEntities = _entityOrderCache.GetOrderedEntities(
            document,
            options.ActiveOwnerBlockId);
        var renderBounds = Direct2DEntityVisibility.ResolveRenderWorldBounds(
            viewport,
            options);
        _parallelVisibleEntities.Clear();
        foreach (var visible in Direct2DEntityVisibility.EnumerateOrderedSubset(
                     document,
                     viewport,
                     options,
                     _resourceCache,
                     orderedEntities,
                     renderBounds))
        {
            _parallelVisibleEntities.Add(visible.Entity);
        }
        return _parallelVisibleEntities;
    }

    internal void RenderBackground(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();
        if (_resourceCache.DeviceContext is not { } context)
            return;

        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        context.Transform = CreateViewportTransform(viewport);
        context.AntialiasMode = options.IsAntialiasingEnabled
            ? AntialiasMode.PerPrimitive
            : AntialiasMode.Aliased;
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (options.DrawGrid)
            {
                _backgroundRenderer.DrawGrid(
                    context,
                    document,
                    viewport,
                    dirtyWorldBounds: null);
            }

            if (options.DrawOrigin)
            {
                _backgroundRenderer.DrawOrigin(
                    context,
                    _resourceCache.Factory,
                    document,
                    viewport,
                    dirtyWorldBounds: null);
            }
        }
        finally
        {
            _statistics.RecordBackgroundRender(ElapsedMilliseconds(started));
            context.AntialiasMode = previousAntialiasMode;
            context.Transform = previousTransform;
        }
    }

    internal void RenderEntityBatch(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(entities);
        ThrowIfDisposed();
        if (_resourceCache.DeviceContext is not { } context)
            return;

        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        context.Transform = CreateViewportTransform(viewport);
        context.AntialiasMode = options.IsAntialiasingEnabled
            ? AntialiasMode.PerPrimitive
            : AntialiasMode.Aliased;
        context.TextAntialiasMode = options.IsTextAntialiasingEnabled
            ? Vortice.Direct2D1.TextAntialiasMode.Default
            : Vortice.Direct2D1.TextAntialiasMode.Aliased;
        context.PrimitiveBlend = PrimitiveBlend.SourceOver;
        using var realizationScaleScope =
            _resourceCache.PushGeometryRealizationScale(viewport.Zoom);
        var started = Stopwatch.GetTimestamp();
        _statistics.RecordScenePass();
        try
        {
            foreach (var entity in entities)
            {
                if (_resourceCache.TryGetEntityResources(entity.Id, out var resources) &&
                    resources is not null)
                {
                    _statistics.RecordVisibleEntity();
                    _statistics.RecordEntitySubmission();
                    DrawEntityCore(
                        context,
                        document,
                        entity,
                        viewport,
                        options,
                        resources);
                }
            }
        }
        finally
        {
            _statistics.RecordEntityRender(ElapsedMilliseconds(started));
            context.PrimitiveBlend = previousPrimitiveBlend;
            context.TextAntialiasMode = previousTextAntialiasMode;
            context.AntialiasMode = previousAntialiasMode;
            context.Transform = previousTransform;
        }
    }

    internal void RenderOverlays(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        Render(
            document,
            viewport,
            transientScene,
            handleScene,
            options,
            Direct2DScenePasses.Overlays);
    }

    private void Render(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions? options,
        Direct2DScenePasses passes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();

        var deviceContext = _resourceCache.DeviceContext;
        if (deviceContext is null)
            return;

        options ??= new CadRenderOptions();
        _statistics.RecordScenePass();

        var previousTransform = deviceContext.Transform;
        var previousAntialiasMode = deviceContext.AntialiasMode;
        var previousTextAntialiasMode = deviceContext.TextAntialiasMode;
        var previousPrimitiveBlend = deviceContext.PrimitiveBlend;
        deviceContext.Transform = CreateViewportTransform(viewport);
        deviceContext.AntialiasMode = options.IsAntialiasingEnabled
            ? AntialiasMode.PerPrimitive
            : AntialiasMode.Aliased;
        deviceContext.TextAntialiasMode = options.IsTextAntialiasingEnabled
            ? Vortice.Direct2D1.TextAntialiasMode.Default
            : Vortice.Direct2D1.TextAntialiasMode.Aliased;
        deviceContext.PrimitiveBlend = PrimitiveBlend.SourceOver;

        try
        {
            var activeLayout = options.ActiveLayoutId is { } layoutId &&
                               document.TryGetLayout(layoutId, out var resolvedLayout)
                ? resolvedLayout
                : null;

            if (activeLayout is not null)
            {
                if ((passes & Direct2DScenePasses.Base) != 0)
                {
                    var backgroundStarted = Stopwatch.GetTimestamp();
                    DrawPaper(deviceContext, activeLayout, options.DrawLayoutGuides);
                    _statistics.RecordBackgroundRender(ElapsedMilliseconds(backgroundStarted));
                    DrawLayoutViewportsBase(
                        deviceContext,
                        document,
                        viewport,
                        activeLayout,
                        transientScene,
                        handleScene,
                        options);
                    var entityStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        DrawEntities(
                            deviceContext,
                            document,
                            viewport,
                            CreatePaperSpaceOptions(activeLayout, options),
                            options.ActiveLayoutViewportId is null ? transientScene : null,
                            options.ActiveLayoutViewportId is null ? handleScene : null);
                    }
                    finally
                    {
                        _statistics.RecordEntityRender(ElapsedMilliseconds(entityStarted));
                    }
                }

                if ((passes & Direct2DScenePasses.Overlays) != 0 &&
                    options.ActiveLayoutViewportId is not null)
                {
                    DrawLayoutViewportOverlays(
                        deviceContext,
                        document,
                        viewport,
                        activeLayout,
                        transientScene,
                        handleScene,
                        options);
                }
            }
            else if ((passes & Direct2DScenePasses.Base) != 0)
            {
                var backgroundStarted = Stopwatch.GetTimestamp();
                try
                {
                    if (options.DrawGrid)
                    {
                        _backgroundRenderer.DrawGrid(
                            deviceContext,
                            document,
                            viewport,
                            options.DirtyWorldBounds);
                    }

                    if (options.DrawOrigin)
                    {
                        _backgroundRenderer.DrawOrigin(
                            deviceContext,
                            _resourceCache.Factory,
                            document,
                            viewport,
                            options.DirtyWorldBounds);
                    }
                }
                finally
                {
                    _statistics.RecordBackgroundRender(ElapsedMilliseconds(backgroundStarted));
                }

                var entityStarted = Stopwatch.GetTimestamp();
                try
                {
                    DrawEntities(
                        deviceContext,
                        document,
                        viewport,
                        options,
                        transientScene,
                        handleScene);
                }
                finally
                {
                    _statistics.RecordEntityRender(ElapsedMilliseconds(entityStarted));
                }
            }

            if ((passes & Direct2DScenePasses.Overlays) != 0 &&
                (activeLayout is null || options.ActiveLayoutViewportId is null))
            {
                var transientStarted = Stopwatch.GetTimestamp();
                try
                {
                    DrawTransients(deviceContext, document, viewport, transientScene, options);
                }
                finally
                {
                    _statistics.RecordTransientRender(ElapsedMilliseconds(transientStarted));
                }

                var selectionStarted = Stopwatch.GetTimestamp();
                try
                {
                    _selectionRenderer.Draw(deviceContext, document, viewport, handleScene, options);
                }
                finally
                {
                    _statistics.RecordSelectionRender(ElapsedMilliseconds(selectionStarted));
                }
            }
        }
        finally
        {
            deviceContext.PrimitiveBlend = previousPrimitiveBlend;
            deviceContext.TextAntialiasMode = previousTextAntialiasMode;
            deviceContext.AntialiasMode = previousAntialiasMode;
            deviceContext.Transform = previousTransform;
        }
    }

    private void DrawPaper(
        ID2D1DeviceContext context,
        CadLayout layout,
        bool drawLayoutGuides)
    {
        var bounds = ToRawRect(layout.PaperBounds);
        var paperBrush = _styleResources.GetBrush(context, layout.PaperColor);
        context.FillRectangle(bounds, paperBrush);
        if (!drawLayoutGuides)
            return;

        var edgeBrush = _styleResources.GetBrush(context, CadColor.FromRgb(64, 64, 64));
        var marginBrush = _styleResources.GetBrush(context, CadColor.FromArgb(217, 115, 115, 115));
        context.DrawRectangle(bounds, edgeBrush, 1f / Math.Max((float)CadEditorZoom(context), 1e-6f));
        context.DrawRectangle(
            ToRawRect(layout.PrintableBounds),
            marginBrush,
            0.75f / Math.Max((float)CadEditorZoom(context), 1e-6f));
    }

    private void DrawLayoutViewportsBase(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayout layout,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        var paperTransform = context.Transform;
        var borderBrush = _styleResources.GetBrush(context, CadColor.FromArgb(230, 51, 115, 204));

        foreach (var layoutViewport in layout.Viewports)
        {
            if (!layoutViewport.IsVisible)
                continue;

            context.Transform = paperTransform;
            context.PushAxisAlignedClip(ToRawRect(layoutViewport.Bounds), AntialiasMode.PerPrimitive);
            try
            {
                var modelToPaper = CreateModelToPaperTransform(layoutViewport);
                context.Transform = modelToPaper * paperTransform;

                var modelViewport = CreateModelViewport(paperViewport, layoutViewport);
                var isActiveViewport = options.ActiveLayoutViewportId == layoutViewport.Id;
                var modelOptions = CreateModelViewportOptions(
                    options,
                    includeHiddenEntities: isActiveViewport);

                var entityStarted = Stopwatch.GetTimestamp();
                try
                {
                    DrawRetainedOrImmediate(
                        context,
                        document,
                        modelViewport,
                        modelOptions,
                        isActiveViewport ? transientScene : null,
                        isActiveViewport ? handleScene : null);
                }
                finally
                {
                    _statistics.RecordEntityRender(ElapsedMilliseconds(entityStarted));
                }

            }
            finally
            {
                context.PopAxisAlignedClip();
                context.Transform = paperTransform;
            }

            if (options.DrawLayoutGuides)
            {
                context.DrawRectangle(
                    ToRawRect(layoutViewport.Bounds),
                    borderBrush,
                    (options.ActiveLayoutViewportId == layoutViewport.Id ? 2f : 1f) /
                    Math.Max((float)paperViewport.Zoom, 1e-6f));
            }
        }
    }

    private void DrawEntities(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene)
    {
        if ((handleScene is null || handleScene.SelectionReferenceCount == 0) &&
            _tileCache.TryDraw(
                context,
                viewport,
                options,
                out var missingTileBounds))
        {
            _statistics.RecordRenderCacheHit();
            foreach (var missingBounds in missingTileBounds)
                DrawMissingTile(context, document, viewport, options, missingBounds);
            return;
        }

        _statistics.RecordRenderCacheMiss();
        DrawRetainedOrImmediate(
            context,
            document,
            viewport,
            options,
            transientScene,
            handleScene);
    }

    private bool PrepareLayoutViewportCaches(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayout layout,
        CadHandleScene? handleScene,
        CadTransientScene? transientScene,
        CadRenderOptions options,
        bool buildStep)
    {
        var preparedViewportCount = 0;
        if (options.ActiveLayoutViewportId is { } activeViewportId)
        {
            foreach (var layoutViewport in layout.Viewports)
            {
                if (!layoutViewport.IsVisible || layoutViewport.Id != activeViewportId)
                    continue;

                if (PrepareLayoutViewportCache(
                        context,
                        document,
                        paperViewport,
                        layoutViewport,
                        handleScene,
                        transientScene,
                        options,
                        isActiveViewport: true,
                        buildStep: buildStep))
                {
                    return true;
                }

                preparedViewportCount++;
                break;
            }
        }

        foreach (var layoutViewport in layout.Viewports)
        {
            if (preparedViewportCount >= MaximumPreparedLayoutViewports)
                break;
            if (!layoutViewport.IsVisible ||
                options.ActiveLayoutViewportId == layoutViewport.Id)
            {
                continue;
            }

            if (PrepareLayoutViewportCache(
                    context,
                    document,
                    paperViewport,
                    layoutViewport,
                    handleScene: null,
                    transientScene: null,
                    options: options,
                    isActiveViewport: false,
                    buildStep: buildStep))
            {
                return true;
            }

            preparedViewportCount++;
        }

        return false;
    }

    private bool PrepareLayoutViewportCache(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayoutViewport layoutViewport,
        CadHandleScene? handleScene,
        CadTransientScene? transientScene,
        CadRenderOptions options,
        bool isActiveViewport,
        bool buildStep)
    {
        var modelViewport = CreateModelViewport(paperViewport, layoutViewport);
        var modelOptions = CreateModelViewportOptions(
            options,
            drawGripHandles: isActiveViewport,
            includeHiddenEntities: isActiveViewport);
        var orderedEntities = _entityOrderCache.GetOrderedEntities(
            document,
            modelOptions.ActiveOwnerBlockId);
        var estimatedRenderWork = _entityOrderCache.GetEstimatedRenderWork(
            document,
            modelOptions.ActiveOwnerBlockId);

        if (!buildStep)
        {
            var transientBuildPending = isActiveViewport &&
                                        PrepareTransientCache(
                                            context,
                                            document,
                                            modelViewport,
                                            transientScene,
                                            modelOptions,
                                            buildStep: false);
            var blockDefinitionBuildPending = _blockReferenceRenderer.PrepareCache(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                buildStep: false);
            var commandListBuildPending = _commandListCache.Prepare(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: false);
            return transientBuildPending ||
                   blockDefinitionBuildPending ||
                   commandListBuildPending;
        }

        if (isActiveViewport &&
            PrepareTransientCache(
                context,
                document,
                modelViewport,
                transientScene,
                modelOptions,
                buildStep: false))
        {
            PrepareTransientCache(
                context,
                document,
                modelViewport,
                transientScene,
                modelOptions,
                buildStep: true);
            return true;
        }

        if (_blockReferenceRenderer.PrepareCache(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                buildStep: false))
        {
            _blockReferenceRenderer.PrepareCache(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                buildStep: true);
            return true;
        }

        if (_commandListCache.Prepare(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: false))
        {
            _commandListCache.Prepare(
                context,
                document,
                modelViewport,
                modelOptions,
                orderedEntities,
                estimatedRenderWork,
                DrawEntityCore,
                buildStep: true);
            return true;
        }

        return false;
    }

    private void DrawRetainedOrImmediate(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        CadTransientScene? transientScene = null,
        CadHandleScene? handleScene = null)
    {
        BuildInlineMovePreviews(transientScene, options.HiddenEntityIds);
        if (_inlineMovePreviews.Count > 0)
        {
            _statistics.RecordRenderCacheMiss();
            var movePacket = _entityOrderCache.GetRenderPacket(
                document,
                options.ActiveOwnerBlockId);
            DrawImmediateWithInlineMovePreviews(
                context,
                document,
                viewport,
                options,
                movePacket,
                handleScene);
            return;
        }

        if (_commandListCache.TryDraw(
                context,
                document,
                viewport,
                options,
                handleScene,
                DrawEntityCore,
                DrawInlineSelectionEntity))
        {
            _statistics.RecordRenderCacheHit();
            return;
        }

        _statistics.RecordRenderCacheMiss();
        var submissionStarted = Stopwatch.GetTimestamp();
        try
        {
            var renderPacket = _entityOrderCache.GetRenderPacket(
                document,
                options.ActiveOwnerBlockId);
            if (handleScene is { SelectionReferenceCount: > 0 })
            {
                DrawImmediateWithInlineSelection(
                    context,
                    document,
                    viewport,
                    options,
                    renderPacket,
                    handleScene);
                return;
            }
            var visibleEntities = Direct2DEntityVisibility.Enumerate(
                document,
                viewport,
                options,
                _resourceCache,
                renderPacket,
                _visibleEntityIds,
                _visibleEntityIdSet,
                _visiblePacketIndices,
                _statistics);
            var entitiesToDraw = visibleEntities;
            if (Direct2DMicroEntityAggregator.ShouldAggregate(
                    renderPacket,
                    options))
            {
                entitiesToDraw = _microEntityAggregator.Aggregate(
                    visibleEntities,
                    renderPacket,
                    viewport,
                    options,
                    out var microCandidateCount,
                    out var microRepresentativeCount);
                _statistics.RecordMicroEntityAggregation(
                    microCandidateCount,
                    microRepresentativeCount);
            }
            foreach (var visible in entitiesToDraw)
            {
                _statistics.RecordVisibleEntity();
                _statistics.RecordEntitySubmission();
                DrawEntityCore(
                    context,
                    document,
                    visible.Entity,
                    viewport,
                    options,
                    visible.Resources);
            }
        }
        finally
        {
            _statistics.RecordCpuEntitySubmission(
                ElapsedMilliseconds(submissionStarted));
        }
    }

    private void DrawLayoutViewportOverlays(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayout layout,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        if (options.ActiveLayoutViewportId is not { } activeViewportId)
            return;

        var layoutViewport = layout.Viewports.FirstOrDefault(
            viewport => viewport.Id == activeViewportId && viewport.IsVisible);
        if (layoutViewport is null)
            return;

        var paperTransform = context.Transform;
        context.PushAxisAlignedClip(ToRawRect(layoutViewport.Bounds), AntialiasMode.PerPrimitive);
        try
        {
            context.Transform =
                CreateModelToPaperTransform(layoutViewport) * paperTransform;
            var modelViewport = CreateModelViewport(paperViewport, layoutViewport);
            var activeModelOptions = CreateModelViewportOptions(
                options,
                drawGripHandles: true,
                includeHiddenEntities: true);

            var transientStarted = Stopwatch.GetTimestamp();
            try
            {
                DrawTransients(
                    context,
                    document,
                    modelViewport,
                    transientScene,
                    activeModelOptions);
            }
            finally
            {
                _statistics.RecordTransientRender(ElapsedMilliseconds(transientStarted));
            }

            var selectionStarted = Stopwatch.GetTimestamp();
            try
            {
                _selectionRenderer.Draw(
                    context,
                    document,
                    modelViewport,
                    handleScene,
                    activeModelOptions);
            }
            finally
            {
                _statistics.RecordSelectionRender(ElapsedMilliseconds(selectionStarted));
            }
        }
        finally
        {
            context.PopAxisAlignedClip();
            context.Transform = paperTransform;
        }
    }

    private void DrawImmediateWithInlineSelection(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DOwnerRenderPacket renderPacket,
        CadHandleScene handleScene)
    {
        var renderBounds = Direct2DEntityVisibility.ResolveRenderWorldBounds(viewport, options);
        foreach (var entry in renderPacket.Entries)
        {
            if (!entry.IsRenderable)
                continue;

            if (handleScene.TryGetSelectionReference(entry.Entity.Id, out var reference) &&
                reference is not null)
            {
                DrawInlineSelectionEntity(context, document, viewport, reference, options);
                continue;
            }

            if (!Direct2DEntityVisibility.TryResolveVisibleEntity(
                    document,
                    viewport,
                    options,
                    _resourceCache,
                    entry.Entity,
                    renderBounds,
                    out var visible))
            {
                continue;
            }

            _statistics.RecordVisibleEntity();
            _statistics.RecordEntitySubmission();
            DrawEntityCore(
                context,
                document,
                visible.Entity,
                viewport,
                options,
                visible.Resources);
        }
    }

    private void DrawImmediateWithInlineMovePreviews(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DOwnerRenderPacket renderPacket,
        CadHandleScene? handleScene)
    {
        var renderBounds = Direct2DEntityVisibility.ResolveRenderWorldBounds(viewport, options);
        foreach (var entry in renderPacket.Entries)
        {
            if (!entry.IsRenderable)
                continue;

            if (_inlineMovePreviews.TryGetValue(entry.Entity.Id, out var preview))
            {
                DrawInlineMovePreview(
                    context,
                    document,
                    viewport,
                    preview,
                    options);
                continue;
            }

            if (handleScene?.TryGetSelectionReference(entry.Entity.Id, out var selection) == true &&
                selection is not null)
            {
                DrawInlineSelectionEntity(context, document, viewport, selection, options);
                continue;
            }

            if (!Direct2DEntityVisibility.TryResolveVisibleEntity(
                    document,
                    viewport,
                    options,
                    _resourceCache,
                    entry.Entity,
                    renderBounds,
                    out var visible))
            {
                continue;
            }

            _statistics.RecordVisibleEntity();
            _statistics.RecordEntitySubmission();
            DrawEntityCore(
                context,
                document,
                visible.Entity,
                viewport,
                options,
                visible.Resources);
        }
    }

    private void DrawInlineMovePreview(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        InlineMovePreview preview,
        CadRenderOptions options)
    {
        var previousTransform = context.Transform;
        context.Transform = ToMatrix3x2(preview.Transform) * previousTransform;
        try
        {
            switch (preview.Item)
            {
                case CadTransientEntityReference reference:
                    _entityReferenceRenderer.Draw(
                        context,
                        document,
                        viewport,
                        reference,
                        options);
                    break;
                case CadTransientBlockReference reference:
                    _blockReferenceRenderer.Draw(
                        context,
                        document,
                        viewport,
                        reference.DefinitionBlockId,
                        reference.Position,
                        reference.RotationRadians,
                        reference.ScaleX,
                        reference.ScaleY,
                        reference.LayerId,
                        reference.ColorSource,
                        reference.GraphicStyleId,
                        options);
                    break;
            }
        }
        finally
        {
            context.Transform = previousTransform;
        }

        _statistics.RecordVisibleEntity();
        _statistics.RecordEntitySubmission();
    }

    private void BuildInlineMovePreviews(
        CadTransientScene? transientScene,
        IReadOnlySet<EntityId> hiddenEntityIds)
    {
        _inlineMovePreviews.Clear();
        if (transientScene is null ||
            transientScene.IsEmpty ||
            hiddenEntityIds.Count == 0)
        {
            return;
        }

        foreach (var item in transientScene.Items)
        {
            if (item is not CadTransientGroup group)
                continue;

            foreach (var child in group.Items)
            {
                var sourceEntityId = child switch
                {
                    CadTransientEntityReference reference => reference.EntityId,
                    CadTransientBlockReference reference => reference.SourceEntityId,
                    _ => null
                };
                if (sourceEntityId is not { } entityId ||
                    !hiddenEntityIds.Contains(entityId))
                {
                    continue;
                }

                _inlineMovePreviews[entityId] = new(child, group.Transform);
            }
        }
    }

    private void DrawInlineSelectionEntity(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        CadRenderOptions options)
    {
        _statistics.RecordVisibleEntity();
        _statistics.RecordEntitySubmission();
        _selectionRenderer.DrawInlineSelectionReference(
            context,
            document,
            viewport,
            reference,
            options);
    }

    private void DrawMissingTile(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        CadRectD tileBounds)
    {
        var renderBounds = options.DirtyWorldBounds is { } dirtyBounds
            ? dirtyBounds.Intersection(tileBounds)
            : tileBounds;
        if (renderBounds.IsEmpty)
            return;

        context.PushAxisAlignedClip(ToRawRect(renderBounds), AntialiasMode.Aliased);
        try
        {
            DrawRetainedOrImmediate(
                context,
                document,
                viewport,
                CreateRegionOptions(options, renderBounds));
        }
        finally
        {
            context.PopAxisAlignedClip();
        }
    }

    private static CadRenderOptions CreateRegionOptions(
        CadRenderOptions source,
        CadRectD dirtyWorldBounds) => new()
        {
            ActiveOwnerBlockId = source.ActiveOwnerBlockId,
            ActiveLayoutId = source.ActiveLayoutId,
            ActiveLayoutViewportId = source.ActiveLayoutViewportId,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsAntialiasingEnabled = source.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = source.IsTextAntialiasingEnabled,
            EnableGeometryRealizations = source.EnableGeometryRealizations,
            IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = source.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = source.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
            EntityStrokeScaleMultiplier = source.EntityStrokeScaleMultiplier,
            HiddenEntityIds = source.HiddenEntityIds,
            DirtyWorldBounds = dirtyWorldBounds,
            EntityBoundsQuery = source.EntityBoundsQuery,
            EntityBoundsQueryInto = source.EntityBoundsQueryInto
        };

    private bool DrawRetainedScene(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        return _commandListCache.TryDraw(
            context,
            document,
            viewport,
            options,
            handleScene: null,
            DrawEntityCore,
            DrawInlineSelectionEntity);
    }

    private void DrawEntityCore(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        CadViewport viewport,
        CadRenderOptions options)
    {
        _resourceCache.TryGetEntityResources(entity.Id, out var resources);
        DrawEntityCore(context, document, entity, viewport, options, resources);
    }

    private void DrawEntityCore(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache.EntityResourceBucket? resources)
    {
        if (entity is CadBlockReference blockReference)
        {
            _statistics.RecordBlockReference();
            _blockReferenceRenderer.Draw(context, document, viewport, blockReference, options);
            return;
        }
        if (entity is CadOleObject oleObject)
        {
            _oleRenderer.DrawEntity(context, document, oleObject, viewport, options);
            return;
        }

        if (resources is not null)
            _entityRenderer.Draw(context, document, entity, resources, viewport, options);
    }

    private static System.Numerics.Matrix3x2 CreateModelToPaperTransform(
        CadLayoutViewport viewport) =>
        System.Numerics.Matrix3x2.CreateTranslation(
            (float)-viewport.ModelCenter.X,
            (float)-viewport.ModelCenter.Y) *
        System.Numerics.Matrix3x2.CreateRotation((float)viewport.RotationRadians) *
        System.Numerics.Matrix3x2.CreateScale((float)viewport.Scale) *
        System.Numerics.Matrix3x2.CreateTranslation(
            (float)viewport.Bounds.Center.X,
            (float)viewport.Bounds.Center.Y);

    private static CadViewport CreateModelViewport(
        CadViewport paperViewport,
        CadLayoutViewport layoutViewport)
    {
        var viewport = new CadViewport();
        viewport.SetSize(paperViewport.ViewWidth, paperViewport.ViewHeight);
        var zoom = Math.Max(paperViewport.Zoom * layoutViewport.Scale, 1e-6);
        var screenCenter = paperViewport.WorldToScreen(layoutViewport.Bounds.Center);
        viewport.SetView(zoom, new CadPointD(
            screenCenter.X - layoutViewport.ModelCenter.X * zoom,
            screenCenter.Y + layoutViewport.ModelCenter.Y * zoom));
        return viewport;
    }

    private static CadRenderOptions CreateModelViewportOptions(
        CadRenderOptions options,
        bool drawGripHandles = false,
        bool includeHiddenEntities = true) => new()
        {
            ActiveOwnerBlockId = BlockId.ModelSpace,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = drawGripHandles,
            IsAntialiasingEnabled = options.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
            EnableGeometryRealizations = options.EnableGeometryRealizations,
            IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = options.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
            EntityStrokeScaleMultiplier = options.EntityStrokeScaleMultiplier,
            EntityBoundsQuery = options.EntityBoundsQuery,
            EntityBoundsQueryInto = options.EntityBoundsQueryInto,
            HiddenEntityIds = includeHiddenEntities
            ? options.HiddenEntityIds
            : CadRenderOptions.NoHiddenEntities
        };

    private static CadRenderOptions CreatePaperSpaceOptions(
        CadLayout layout,
        CadRenderOptions options) => new()
        {
            ActiveOwnerBlockId = layout.PaperSpaceBlockId,
            ActiveLayoutId = layout.Id,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = options.DrawGripHandles,
            IsAntialiasingEnabled = options.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
            EnableGeometryRealizations = options.EnableGeometryRealizations,
            IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = options.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
            EntityStrokeScaleMultiplier = options.EntityStrokeScaleMultiplier,
            HiddenEntityIds = options.HiddenEntityIds,
            DirtyWorldBounds = options.DirtyWorldBounds,
            EntityBoundsQuery = options.EntityBoundsQuery,
            EntityBoundsQueryInto = options.EntityBoundsQueryInto
        };

    private static System.Numerics.Matrix3x2 ToMatrix3x2(CadMatrixD matrix) => new(
        (float)matrix.M11,
        (float)matrix.M12,
        (float)matrix.M21,
        (float)matrix.M22,
        (float)matrix.OffsetX,
        (float)matrix.OffsetY);

    private static RawRectF ToRawRect(CadRectD bounds) => new(
        (float)bounds.MinX,
        (float)bounds.MinY,
        (float)bounds.MaxX,
        (float)bounds.MaxY);

    private static double CadEditorZoom(ID2D1DeviceContext context)
    {
        var transform = context.Transform;
        return Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
    }

    private static double ElapsedMilliseconds(long started) =>
        Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private void DrawTransients(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene,
        CadRenderOptions options)
    {
        if (scene is null || scene.IsEmpty)
            _oleRenderer.ClearTransient();
        else
            _oleRenderer.ReconcileTransient(scene);

        _transientSceneRenderer.Draw(
            deviceContext,
            document,
            viewport,
            scene,
            options,
            ole => _oleRenderer.DrawTransient(deviceContext, ole, viewport, options),
            reference => _entityReferenceRenderer.Draw(
                deviceContext,
                document,
                viewport,
                reference,
                options),
            reference => _blockReferenceRenderer.Draw(
                deviceContext,
                document,
                viewport,
                reference.DefinitionBlockId,
                reference.Position,
                 reference.RotationRadians,
                 reference.ScaleX,
                 reference.ScaleY,
                 reference.LayerId,
                 reference.ColorSource,
                reference.GraphicStyleId,
                options),
            group => IsInlineMoveGroup(group, options.HiddenEntityIds));
    }

    private static bool IsInlineMoveGroup(
        CadTransientGroup group,
        IReadOnlySet<EntityId> hiddenEntityIds)
    {
        if (hiddenEntityIds.Count == 0 || group.Items.Count == 0)
            return false;

        var hasReference = false;
        foreach (var child in group.Items)
        {
            var sourceEntityId = child switch
            {
                CadTransientEntityReference reference => reference.EntityId,
                CadTransientBlockReference reference => reference.SourceEntityId,
                _ => null
            };
            if (sourceEntityId is not { } entityId)
                return false;

            hasReference = true;
            if (!hiddenEntityIds.Contains(entityId))
                return false;
        }

        return hasReference;
    }

    private bool PrepareTransientCache(
        ID2D1DeviceContext deviceContext,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene,
        CadRenderOptions options,
        bool buildStep)
    {
        if (options.HiddenEntityIds.Count > 0)
            return false;

        return _transientSceneRenderer.PrepareCache(
            deviceContext,
            document,
            viewport,
            scene,
            options,
            reference => _entityReferenceRenderer.Draw(
                deviceContext,
                document,
                viewport,
                reference,
                options),
            reference => _blockReferenceRenderer.Draw(
                deviceContext,
                document,
                viewport,
                reference.DefinitionBlockId,
                reference.Position,
                reference.RotationRadians,
                reference.ScaleX,
                reference.ScaleY,
                reference.LayerId,
                reference.ColorSource,
                reference.GraphicStyleId,
                options),
            buildStep);
    }

    private static System.Numerics.Matrix3x2 CreateViewportTransform(CadViewport viewport)
    {
        return System.Numerics.Matrix3x2.CreateScale((float)viewport.Zoom, (float)-viewport.Zoom) *
               System.Numerics.Matrix3x2.CreateTranslation(
                   (float)viewport.Offset.X,
                   (float)viewport.Offset.Y);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _tileCache.Dispose();
        _commandListCache.Dispose();
        _backgroundRenderer.Dispose();
        _blockReferenceRenderer.Dispose();
        _entityOrderCache.Dispose();
        _resourceCache.Dispose();
        _transientSceneRenderer.Dispose();
        _oleRenderer.Dispose();
        _textFormatResources.Dispose();
        _styleResources.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DSceneRender));
    }

    private readonly record struct InlineMovePreview(
        CadTransientItem Item,
        CadMatrixD Transform);
}

[Flags]
internal enum Direct2DScenePasses
{
    Base = 1,
    Overlays = 2,
    All = Base | Overlays
}
