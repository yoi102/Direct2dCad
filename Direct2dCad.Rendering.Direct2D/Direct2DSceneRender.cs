using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

public sealed class Direct2DSceneRender : CadRender, ICadGeometryResourceManager, IDisposable
{
    private readonly Direct2DResourceCache _resourceCache = new();
    private readonly Direct2DBackgroundRenderer _backgroundRenderer = new();
    private readonly Direct2DTransientSceneRenderer _transientSceneRenderer;
    private readonly Direct2DSelectionRenderer _selectionRenderer;
    private readonly Direct2DEntityRenderer _entityRenderer;
    private readonly Direct2DOleRenderer _oleRenderer;
    private readonly Direct2DEntityReferenceRenderer _entityReferenceRenderer;
    private bool _disposed;

    public Direct2DSceneRender()
    {
        var geometryFactory = new Direct2DGeometryFactory();
        var styleResourceFactory = new Direct2DStyleResourceFactory();
        var transientRenderer = new Direct2DTransientRenderer(
            _resourceCache,
            geometryFactory,
            styleResourceFactory);
        var handleRenderer = new Direct2DHandleRenderer();

        _transientSceneRenderer = new Direct2DTransientSceneRenderer(
            transientRenderer,
            new Direct2DTransientImageCache());
        _selectionRenderer = new Direct2DSelectionRenderer(
            _resourceCache,
            transientRenderer,
            styleResourceFactory,
            handleRenderer);
        _entityRenderer = new Direct2DEntityRenderer(
            _resourceCache,
            geometryFactory,
            styleResourceFactory);
        _oleRenderer = new Direct2DOleRenderer(_resourceCache);
        _entityReferenceRenderer = new Direct2DEntityReferenceRenderer(
            _resourceCache,
            _entityRenderer,
            transientRenderer,
            _oleRenderer);
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
        _transientSceneRenderer.Clear();
        _oleRenderer.Clear();
        _resourceCache.ResetDeviceResources(factory, writeFactory, deviceContext, document);
    }

    public void RebuildAll(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _resourceCache.RebuildAll(document);
    }

    public void RebuildEntity(CadDocument document, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        _resourceCache.RebuildEntityResources(document, entityId);
    }

    public void RemoveEntity(EntityId entityId)
    {
        ThrowIfDisposed();
        _resourceCache.RemoveEntity(entityId);
        _oleRenderer.RemoveEntity(entityId);
    }

    public void InvalidateOleBitmap(EntityId entityId)
    {
        ThrowIfDisposed();
        _oleRenderer.RemoveEntity(entityId);
    }

    public void CompleteFrame()
    {
        _oleRenderer.CompleteFrame();
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
        foreach (var layoutViewport in layout.Viewports.Where(item => item.IsVisible))
        {
            var modelViewport = CreateModelViewport(viewport, layoutViewport);
            var modelOptions = CreateModelViewportOptions(options);
            var modelToScreen = CreateModelToPaperTransform(layoutViewport) * paperTransform;
            foreach (var ole in Direct2DEntityVisibility
                         .Enumerate(document, modelViewport, modelOptions, _resourceCache)
                         .OfType<CadOleObject>())
            {
                _oleRenderer.PrepareEntityTiles(context, ole, viewport, modelToScreen);
            }

            if (options.ActiveLayoutViewportId == layoutViewport.Id && transientScene is not null)
            {
                foreach (var transientOle in transientScene.Items.OfType<CadTransientOleObject>())
                    _oleRenderer.PrepareTransientTiles(context, transientOle, viewport, modelToScreen);
            }
        }
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
        using var paperBrush = context.CreateSolidColorBrush(ToColor4(layout.PaperColor));
        using var edgeBrush = context.CreateSolidColorBrush(new Color4(0.25f, 0.25f, 0.25f, 1f));
        using var marginBrush = context.CreateSolidColorBrush(new Color4(0.45f, 0.45f, 0.45f, 0.85f));
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
        using var borderBrush = context.CreateSolidColorBrush(new Color4(0.2f, 0.45f, 0.8f, 0.9f));

        foreach (var layoutViewport in layout.Viewports.Where(item => item.IsVisible))
        {
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
                             _resourceCache))
                {
                    if (entity is CadOleObject ole)
                    {
                        _oleRenderer.DrawEntity(context, ole, paperViewport);
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
        foreach (var entity in Direct2DEntityVisibility.Enumerate(document, viewport, options, _resourceCache))
        {
            if (entity is CadOleObject oleObject)
            {
                _oleRenderer.DrawEntity(context, oleObject, viewport);
                continue;
            }

            if (_resourceCache.TryGetEntityResources(entity.Id, out var resources) && resources is not null)
                _entityRenderer.Draw(context, document, entity, resources, viewport, options);
        }
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
        KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
        HiddenEntityIds = includeHiddenEntities ? options.HiddenEntityIds : new HashSet<EntityId>()
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
        KeepStrokeWidthScreenConstant = options.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
        HiddenEntityIds = options.HiddenEntityIds,
        DirtyWorldBounds = options.DirtyWorldBounds
    };

    private static RawRectF ToRawRect(CadRectD bounds) => new(
        (float)bounds.MinX,
        (float)bounds.MinY,
        (float)bounds.MaxX,
        (float)bounds.MaxY);

    private static Color4 ToColor4(CadColor color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    private static double CadEditorZoom(ID2D1DeviceContext context)
    {
        var transform = context.Transform;
        return Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
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
            ole => _oleRenderer.DrawTransient(deviceContext, ole, viewport),
            reference => _entityReferenceRenderer.Draw(
                deviceContext,
                document,
                viewport,
                reference,
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

        _resourceCache.Dispose();
        _transientSceneRenderer.Dispose();
        _oleRenderer.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DSceneRender));
    }
}
