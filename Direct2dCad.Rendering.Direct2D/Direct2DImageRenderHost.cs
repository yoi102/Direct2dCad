using Direct2dCad.Common;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

public sealed class Direct2DImageRenderHost : IDisposable
{
    private readonly ImageSourceDirect2DResource _target = new();
    private readonly Direct2DSceneRender _renderer = new();
    private ID3D11ImageSource? _imageSource;
    private CadDocument? _document;
    private CadViewport? _viewport;
    private CadTransientScene? _transientScene;
    private CadHandleScene? _handleScene;
    private CadRenderOptions _renderOptions = new();
    private bool _disposed;

    public ICadGeometryResourceManager GeometryResourceManager => _renderer;

    public Color4 FallbackBackgroundColor { get; set; } = new(0.08f, 0.09f, 0.10f, 1.0f);

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
        Render();
    }

    public void SetHandleScene(CadHandleScene? handleScene)
    {
        ThrowIfDisposed();

        _handleScene = handleScene;
        Render();
    }

    public void SetRenderOptions(CadRenderOptions? renderOptions)
    {
        ThrowIfDisposed();

        _renderOptions = renderOptions ?? new CadRenderOptions();
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
            return;

        _imageSource?.SetSize(width, height);

        if (_imageSource is not null)
            _target.SetSize(width, height);

        Render();
    }

    public void Render()
    {
        ThrowIfDisposed();

        if (!_target.IsTargetReady)
            return;

        _target.DrawFrame(context =>
        {
            context.Clear(_document is null
                ? FallbackBackgroundColor
                : ToColor4(_document.ViewSettings.BackgroundColor));

            if (_document is not null && _viewport is not null)
                _renderer.Render(_document, _viewport, _transientScene, _handleScene, _renderOptions);
        });
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
