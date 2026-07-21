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
    private readonly Direct2DCommandListChunkCache _commandListCache;
    private readonly Direct2DSceneTileCache _tileCache;
    private bool _disposed;

    public CadRenderStatistics RenderStatistics { get; private set; } = CadRenderStatistics.Empty;

    public Direct2DSceneRender()
    {
        _resourceCache = new Direct2DResourceCache(_styleResources, _textFormatResources);
        var geometryFactory = new Direct2DGeometryFactory();
        var transientRenderer = new Direct2DTransientRenderer(
            _resourceCache,
            geometryFactory,
            _styleResources,
            _textFormatResources);
        var handleRenderer = new Direct2DHandleRenderer(_styleResources);

        _backgroundRenderer = new Direct2DBackgroundRenderer(_styleResources);
        _transientSceneRenderer = new Direct2DTransientSceneRenderer(
            transientRenderer,
            new Direct2DTransientImageCache());
        _selectionRenderer = new Direct2DSelectionRenderer(
            _resourceCache,
            transientRenderer,
            _styleResources,
            handleRenderer,
            _entityOrderCache,
            _statistics);
        _entityRenderer = new Direct2DEntityRenderer(
            _resourceCache,
            geometryFactory,
            _styleResources);
        _oleRenderer = new Direct2DOleRenderer(
            _resourceCache,
            _entityOrderCache,
            _styleResources);
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
        _tileCache = new Direct2DSceneTileCache(_statistics);
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
        _tileCache.ApplyChanges(document, changes);
        _commandListCache.ApplyChanges(document, changes);
        if (AffectsEntityOrder(changes))
            _entityOrderCache.Invalidate();
        else if (AffectsOwnerMetrics(changes))
            _entityOrderCache.InvalidateOwnerMetrics();
        _resourceCache.ApplyChanges(document, changes);
        _oleRenderer.ApplyChanges(document, changes);
    }

    public void ResetDeviceResources(
        ID2D1Factory? factory,
        IDWriteFactory? writeFactory,
        ID2D1DeviceContext? deviceContext,
        CadDocument? document = null)
    {
        ThrowIfDisposed();
        _tileCache.Clear();
        _commandListCache.Clear();
        _entityOrderCache.Invalidate();
        _transientSceneRenderer.Clear();
        _oleRenderer.Clear();
        // Release entity leases before the device-bound shared caches are reset.
        _resourceCache.ClearCache();
        _styleResources.Reset(factory, deviceContext);
        _textFormatResources.Reset(writeFactory);
        _resourceCache.ResetDeviceResources(factory, writeFactory, deviceContext, document);
    }

    public void RebuildAll(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _tileCache.Clear();
        _commandListCache.Clear();
        _entityOrderCache.Invalidate();
        _resourceCache.RebuildAll(document);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _tileCache.InvalidateEntity(document, entityId);
        _commandListCache.InvalidateEntity(entityId);
        _entityOrderCache.Invalidate();
        _resourceCache.RebuildEntityResources(document, entityId);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        _tileCache.RemoveEntity(entityId);
        _commandListCache.Clear();
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

    public void BeginFrame(bool isFullFrame = true, int dirtyRegionCount = 1)
    {
        ThrowIfDisposed();
        _statistics.BeginFrame(isFullFrame, dirtyRegionCount);
        _resourceCache.BeginFrame();
        _styleResources.BeginFrame();
        _textFormatResources.BeginFrame();
    }

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
                foreach (var ole in Direct2DEntityVisibility
                             .Enumerate(
                                 document,
                                 modelViewport,
                                 modelOptions,
                                 _resourceCache,
                                 orderedOleEntities,
                                 _entityOrderCache)
                             .Cast<CadOleObject>())
                {
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
        bool buildStep = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ThrowIfDisposed();
        if (_resourceCache.DeviceContext is not { } context)
            return false;

        options ??= new CadRenderOptions();
        var orderedEntities = _entityOrderCache.GetOrderedEntities(
            document,
            options.ActiveOwnerBlockId);
        var estimatedRenderWork = _entityOrderCache.GetEstimatedRenderWork(
            document,
            options.ActiveOwnerBlockId);
        var commandListBuildPending = _commandListCache.Prepare(
            context,
            document,
            viewport,
            options,
            orderedEntities,
            estimatedRenderWork,
            DrawEntityCore,
            buildStep);
        if (commandListBuildPending)
            return true;

        return _tileCache.Prepare(
            context,
            document,
            viewport,
            options,
            estimatedRenderWork,
            DrawRetainedScene,
            buildStep);
    }

    public override void Render(CadDocument document, CadViewport viewport, CadRenderOptions? options = null)
    {
        Render(document, viewport, null, null, options);
    }

    public void Render(
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene = null,
        CadRenderOptions? options = null)
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
                DrawPaper(deviceContext, activeLayout);
                DrawLayoutViewports(
                    deviceContext,
                    document,
                    viewport,
                    activeLayout,
                    transientScene,
                    handleScene,
                    options);
                DrawEntities(
                    deviceContext,
                    document,
                    viewport,
                    CreatePaperSpaceOptions(activeLayout, options));
            }
            else if (options.DrawGrid)
                _backgroundRenderer.DrawGrid(deviceContext, document, viewport, options.DirtyWorldBounds);

            if (activeLayout is null && options.DrawOrigin)
            {
                _backgroundRenderer.DrawOrigin(
                    deviceContext,
                    _resourceCache.Factory,
                    document,
                    viewport,
                    options.DirtyWorldBounds);
            }

            if (activeLayout is null)
                DrawEntities(deviceContext, document, viewport, options);

            if (activeLayout is null || options.ActiveLayoutViewportId is null)
            {
                DrawTransients(deviceContext, document, viewport, transientScene, options);
                _selectionRenderer.Draw(deviceContext, document, viewport, handleScene, options);
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

    private void DrawPaper(ID2D1DeviceContext context, CadLayout layout)
    {
        var bounds = ToRawRect(layout.PaperBounds);
        var paperBrush = _styleResources.GetBrush(context, layout.PaperColor);
        var edgeBrush = _styleResources.GetBrush(context, CadColor.FromRgb(64, 64, 64));
        var marginBrush = _styleResources.GetBrush(context, CadColor.FromArgb(217, 115, 115, 115));
        context.FillRectangle(bounds, paperBrush);
        context.DrawRectangle(bounds, edgeBrush, 1f / Math.Max((float)CadEditorZoom(context), 1e-6f));
        context.DrawRectangle(
            ToRawRect(layout.PrintableBounds),
            marginBrush,
            0.75f / Math.Max((float)CadEditorZoom(context), 1e-6f));
    }

    private void DrawLayoutViewports(
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

                foreach (var entity in Direct2DEntityVisibility.Enumerate(
                             document,
                             modelViewport,
                             modelOptions,
                             _resourceCache,
                             _entityOrderCache.GetOrderedEntities(
                                 document,
                                 modelOptions.ActiveOwnerBlockId),
                             _entityOrderCache))
                {
                    if (entity is CadBlockReference blockReference)
                    {
                        _blockReferenceRenderer.Draw(context, document, modelViewport, blockReference, modelOptions);
                        continue;
                    }
                    if (entity is CadOleObject ole)
                    {
                        _oleRenderer.DrawEntity(
                            context,
                            document,
                            ole,
                            paperViewport,
                            modelOptions);
                        continue;
                    }
                    if (_resourceCache.TryGetEntityResources(entity.Id, out var resources) && resources is not null)
                        _entityRenderer.Draw(context, document, entity, resources, modelViewport, modelOptions);
                }

                if (isActiveViewport)
                {
                    var activeModelOptions = CreateModelViewportOptions(options, drawGripHandles: true);
                    DrawTransients(context, document, modelViewport, transientScene, activeModelOptions);
                    _selectionRenderer.Draw(
                        context,
                        document,
                        modelViewport,
                        handleScene,
                        activeModelOptions);
                }
            }
            finally
            {
                context.PopAxisAlignedClip();
                context.Transform = paperTransform;
            }

            context.DrawRectangle(
                ToRawRect(layoutViewport.Bounds),
                borderBrush,
                (options.ActiveLayoutViewportId == layoutViewport.Id ? 2f : 1f) /
                Math.Max((float)paperViewport.Zoom, 1e-6f));
        }
    }

    private void DrawEntities(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (_tileCache.TryDraw(
                context,
                viewport,
                options,
                out var missingTileBounds))
        {
            foreach (var missingBounds in missingTileBounds)
                DrawMissingTile(context, document, viewport, options, missingBounds);
            return;
        }

        DrawRetainedOrImmediate(context, document, viewport, options);
    }

    private void DrawRetainedOrImmediate(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (_commandListCache.TryDraw(
                context,
                document,
                viewport,
                options,
                DrawEntityCore))
        {
            return;
        }

        foreach (var entity in Direct2DEntityVisibility.Enumerate(
                     document,
                     viewport,
                     options,
                     _resourceCache,
                     _entityOrderCache.GetOrderedEntities(document, options.ActiveOwnerBlockId),
                     _entityOrderCache))
        {
            _statistics.RecordVisibleEntity();
            _statistics.RecordEntitySubmission();
            DrawEntityCore(context, document, entity, viewport, options);
        }
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
        IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
        AllowApproximateScaleFallback = source.AllowApproximateScaleFallback,
        KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
        HiddenEntityIds = source.HiddenEntityIds,
        DirtyWorldBounds = dirtyWorldBounds,
        EntityBoundsQuery = source.EntityBoundsQuery
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
            DrawEntityCore);
    }

    private void DrawEntityCore(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        CadViewport viewport,
        CadRenderOptions options)
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

        if (_resourceCache.TryGetEntityResources(entity.Id, out var resources) && resources is not null)
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
        IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
        AllowApproximateScaleFallback = options.AllowApproximateScaleFallback,
        KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
        EntityBoundsQuery = options.EntityBoundsQuery,
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
        IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
        AllowApproximateScaleFallback = options.AllowApproximateScaleFallback,
        KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
        HiddenEntityIds = options.HiddenEntityIds,
        DirtyWorldBounds = options.DirtyWorldBounds,
        EntityBoundsQuery = options.EntityBoundsQuery
    };

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

    private static bool AffectsEntityOrder(CadDocumentChangeSet changes)
    {
        if (changes.AffectsDocumentStructure)
            return true;

        const CadEntityChangeKind orderChanges =
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted |
            CadEntityChangeKind.DrawOrder |
            CadEntityChangeKind.Layer;
        return changes.EntityChanges.Any(change => (change.Kind & orderChanges) != 0);
    }

    private static bool AffectsOwnerMetrics(CadDocumentChangeSet changes)
    {
        const CadEntityChangeKind relevantChanges =
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Visibility;
        return changes.EntityChanges.Any(change =>
            (change.Kind & relevantChanges) != 0);
    }

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
                 options));
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
}
