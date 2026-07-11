using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

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
        _oleRenderer.PrepareTiles(document, viewport, transientScene, options ?? new CadRenderOptions());
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
            if (options.DrawGrid)
                _backgroundRenderer.DrawGrid(deviceContext, document, viewport, options.DirtyWorldBounds);

            if (options.DrawOrigin)
            {
                _backgroundRenderer.DrawOrigin(
                    deviceContext,
                    _resourceCache.Factory,
                    document,
                    viewport,
                    options.DirtyWorldBounds);
            }

            foreach (var entity in Direct2DEntityVisibility.Enumerate(document, viewport, options, _resourceCache))
            {
                if (entity is CadOleObject oleObject)
                {
                    _oleRenderer.DrawEntity(deviceContext, oleObject, viewport);
                    continue;
                }

                if (!_resourceCache.TryGetEntityResources(entity.Id, out var resources) || resources is null)
                    continue;

                _entityRenderer.Draw(deviceContext, document, entity, resources, viewport, options);
            }

            DrawTransients(deviceContext, document, viewport, transientScene, options);
            _selectionRenderer.Draw(deviceContext, document, viewport, handleScene, options);
        }
        finally
        {
            deviceContext.PrimitiveBlend = previousPrimitiveBlend;
            deviceContext.TextAntialiasMode = previousTextAntialiasMode;
            deviceContext.AntialiasMode = previousAntialiasMode;
            deviceContext.Transform = previousTransform;
        }
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
