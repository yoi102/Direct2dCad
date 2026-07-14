using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

public sealed class Direct2DImageRenderHost : IDisposable
{
    private const double PartialRenderMaxAreaRatio = 0.65;
    private readonly ImageSourceDirect2DResource _target = new();
    private readonly Direct2DSceneRender _renderer = new();
    private ID3D11ImageSource? _imageSource;
    private CadDocument? _document;
    private CadViewport? _viewport;
    private CadTransientScene? _transientScene;
    private CadHandleScene? _handleScene;
    private CadRenderOptions _renderOptions = new();
    private ID2D1DeviceContext? _clearBrushContext;
    private ID2D1SolidColorBrush? _clearBrush;
    private Color4 _clearBrushColor;
    private bool _disposed;

    public ICadGeometryResourceManager GeometryResourceManager => _renderer;

    public int TargetWidth => _target.Width;

    public int TargetHeight => _target.Height;

    public Color4 FallbackBackgroundColor { get; set; } = new(0.08f, 0.09f, 0.10f, 1.0f);

    public CadDocumentChangeSet UpdateTextMeasurements(CadDocument document)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);

        var changedIds = new List<EntityId>();
        foreach (var text in document.Entities.Values.OfType<CadText>())
        {
            if (text.IsErased || !text.RequiresBoundsMeasurement)
                continue;

            if (Direct2DTextServices.TryMeasureTextBounds(
                    _target.DwriteFactory,
                    document,
                    text,
                    out var localBounds) &&
                text.SetLocalBounds(localBounds))
            {
                changedIds.Add(text.Id);
            }
        }

        return changedIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(changedIds, CadEntityChangeKind.Geometry);
    }

    public bool TryMeasureTextBounds(
        CadDocument document,
        string text,
        CadPointD position,
        double height,
        StyleId? textStyleId,
        out CadRectD bounds)
    {
        ThrowIfDisposed();

        if (Direct2DTextServices.TryMeasureTextBounds(
                _target.DwriteFactory,
                document,
                text,
                height,
                textStyleId,
                out var localBounds))
        {
            bounds = localBounds.Translate(position - CadPointD.Origin);
            return true;
        }

        bounds = CadRectD.Empty;
        return false;
    }

    public void AttachImageSource(ID3D11ImageSource imageSource)
    {
        ThrowIfDisposed();

        _imageSource = imageSource ?? throw new ArgumentNullException(nameof(imageSource));
        _target.SetTarget(_imageSource);
        ResetRendererDeviceResources();
    }

    public void SetScene(CadDocument document, CadViewport viewport)
    {
        ThrowIfDisposed();

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        ResetRendererDeviceResources();
    }

    public void SetTransientScene(CadTransientScene? transientScene)
    {
        ThrowIfDisposed();

        _transientScene = transientScene;
        Render(CadRenderInvalidation.Full);
    }

    public void SetHandleScene(CadHandleScene? handleScene)
    {
        ThrowIfDisposed();

        _handleScene = handleScene;
        Render(CadRenderInvalidation.Full);
    }

    public void SetRenderOptions(CadRenderOptions? renderOptions)
    {
        ThrowIfDisposed();

        _renderOptions = renderOptions ?? new CadRenderOptions();
    }

    public void SetOleDrawCallback(Direct2DOleDrawCallback? callback)
    {
        ThrowIfDisposed();
        _renderer.OleDrawCallback = callback;
    }

    public void SetOleReleaseCallback(Direct2DOleReleaseCallback? callback)
    {
        ThrowIfDisposed();
        _renderer.OleReleaseCallback = callback;
    }

    public void InvalidateOleBitmap(EntityId entityId)
    {
        ThrowIfDisposed();
        _renderer.InvalidateOleBitmap(entityId);
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
            return;

        _imageSource?.SetSize(width, height);

        if (_imageSource is not null)
            _target.SetSize(width, height);

        Render(CadRenderInvalidation.Full);
    }

    public void Render(CadRenderInvalidation? invalidation = null)
    {
        RenderCore(invalidation, retryAfterDeviceResourceRecreation: true);
    }

    private void RenderCore(
        CadRenderInvalidation? invalidation,
        bool retryAfterDeviceResourceRecreation)
    {
        ThrowIfDisposed();

        if (!_target.IsTargetReady)
            return;

        var effectiveInvalidation = NormalizeInvalidation(invalidation);
        if (effectiveInvalidation.IsEmpty)
            return;

        var background = _document is null
            ? FallbackBackgroundColor
            : ToColor4(_document.ViewSettings.BackgroundColor);

        try
        {
            _renderer.BeginFrame();
            try
            {
                if (_document is not null && _viewport is not null)
                {
                    _renderer.PrepareOleTiles(
                        _document,
                        _viewport,
                        _transientScene,
                        _renderOptions);
                }

                _target.DrawFrame(context =>
                {
                    if (!effectiveInvalidation.IsFull)
                    {
                        foreach (var dirty in effectiveInvalidation.DirtyScreenRects)
                        {
                            var clip = ToRawRectF(dirty);
                            var previousTransform = context.Transform;
                            context.Transform = System.Numerics.Matrix3x2.Identity;
                            context.PushAxisAlignedClip(clip, AntialiasMode.Aliased);

                            try
                            {
                                FillScreenRect(context, clip, background);

                                if (_document is not null && _viewport is not null)
                                {
                                    var dirtyWorldBounds = ScreenRectToWorldBounds(dirty, _viewport);
                                    _renderer.Render(
                                        _document,
                                        _viewport,
                                        _transientScene,
                                        _handleScene,
                                        CreateRenderOptions(dirtyWorldBounds));
                                }
                            }
                            finally
                            {
                                context.PopAxisAlignedClip();
                                context.Transform = previousTransform;
                            }
                        }

                        return;
                    }

                    context.Clear(background);

                    if (_document is not null && _viewport is not null)
                        _renderer.Render(_document, _viewport, _transientScene, _handleScene, _renderOptions);
                }, effectiveInvalidation.IsFull ? null : effectiveInvalidation.DirtyScreenRects);
            }
            finally
            {
                _renderer.CompleteFrame();
            }
        }
        catch (Direct2DDeviceResourcesRecreatedException) when (retryAfterDeviceResourceRecreation)
        {
            ResetRendererDeviceResources();
            RenderCore(CadRenderInvalidation.Full, retryAfterDeviceResourceRecreation: false);
        }
    }

    private CadRenderInvalidation NormalizeInvalidation(CadRenderInvalidation? invalidation)
    {
        if (invalidation is null || invalidation.IsFull)
            return CadRenderInvalidation.Full;

        if (_target.Width <= 0 || _target.Height <= 0)
            return CadRenderInvalidation.Full;

        var rects = new List<CadScreenRect>(invalidation.DirtyScreenRects.Count);
        foreach (var dirtyRect in invalidation.DirtyScreenRects)
        {
            var rect = ClampToTarget(dirtyRect);
            if (!rect.IsEmpty)
                rects.Add(rect);
        }

        if (rects.Count == 0)
            return CadRenderInvalidation.FromScreenRect(default);

        var normalizedInvalidation = CadRenderInvalidation.FromScreenRects(rects);

        var area = normalizedInvalidation.DirtyScreenRects.Sum(static rect => (double)rect.Area);
        var targetArea = (double)_target.Width * _target.Height;
        return targetArea > 0 && area / targetArea >= PartialRenderMaxAreaRatio
            ? CadRenderInvalidation.Full
            : normalizedInvalidation;
    }

    private CadScreenRect ClampToTarget(CadScreenRect rect)
    {
        if (rect.IsEmpty || _target.Width <= 0 || _target.Height <= 0)
            return default;

        var x = Math.Clamp(rect.X, 0, _target.Width);
        var y = Math.Clamp(rect.Y, 0, _target.Height);
        var right = Math.Clamp(rect.X + rect.Width, 0, _target.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, _target.Height);
        return new CadScreenRect(x, y, right - x, bottom - y);
    }

    private CadRenderOptions CreateRenderOptions(CadRectD? dirtyWorldBounds)
    {
        return new CadRenderOptions
        {
            ActiveOwnerBlockId = _renderOptions.ActiveOwnerBlockId,
            ActiveLayoutId = _renderOptions.ActiveLayoutId,
            ActiveLayoutViewportId = _renderOptions.ActiveLayoutViewportId,
            DrawGrid = _renderOptions.DrawGrid,
            DrawOrigin = _renderOptions.DrawOrigin,
            DrawGripHandles = _renderOptions.DrawGripHandles,
            IsAntialiasingEnabled = _renderOptions.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = _renderOptions.IsTextAntialiasingEnabled,
            KeepStrokeWidthScreenConstant = _renderOptions.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = _renderOptions.MinimumScreenStrokeWidth,
            HiddenEntityIds = _renderOptions.HiddenEntityIds,
            DirtyWorldBounds = dirtyWorldBounds
        };
    }

    private static CadRectD ScreenRectToWorldBounds(CadScreenRect rect, CadViewport viewport)
    {
        return CadRectD.Empty
            .ExpandToInclude(viewport.ScreenToWorld(new CadPointD(rect.X, rect.Y)))
            .ExpandToInclude(viewport.ScreenToWorld(new CadPointD(rect.X + rect.Width, rect.Y + rect.Height)));
    }

    private static RawRectF ToRawRectF(CadScreenRect rect)
    {
        return new RawRectF(
            rect.X,
            rect.Y,
            rect.X + rect.Width,
            rect.Y + rect.Height);
    }

    private void FillScreenRect(ID2D1DeviceContext context, RawRectF rect, Color4 color)
    {
        var previousTransform = context.Transform;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        context.Transform = System.Numerics.Matrix3x2.Identity;
        context.PrimitiveBlend = PrimitiveBlend.Copy;

        try
        {
            context.FillRectangle(rect, GetClearBrush(context, color));
        }
        finally
        {
            context.PrimitiveBlend = previousPrimitiveBlend;
            context.Transform = previousTransform;
        }
    }

    private ID2D1SolidColorBrush GetClearBrush(ID2D1DeviceContext context, Color4 color)
    {
        if (_clearBrush is not null &&
            ReferenceEquals(_clearBrushContext, context) &&
            _clearBrushColor.Equals(color))
        {
            return _clearBrush;
        }

        ReleaseClearBrush();
        _clearBrushContext = context;
        _clearBrushColor = color;
        _clearBrush = context.CreateSolidColorBrush(color);
        return _clearBrush;
    }

    private void ReleaseClearBrush()
    {
        _clearBrush?.Dispose();
        _clearBrush = null;
        _clearBrushContext = null;
    }

    private static Color4 ToColor4(CadColor color)
    {
        return new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f);
    }

    private void ResetRendererDeviceResources()
    {
        if (!_target.IsTargetReady)
            return;

        ReleaseClearBrush();
        _renderer.ResetDeviceResources(
            _target.Factory,
            _target.DwriteFactory,
            _target.Context,
            _document);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseClearBrush();
        _renderer.Dispose();
        _target.Dispose();
        _imageSource = null;
        _document = null;
        _viewport = null;
        _transientScene = null;
        _handleScene = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DImageRenderHost));
    }
}
