using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DirectWrite;
using Vortice.DXGI;

using D3D9Api = Vortice.Direct3D9.D3D9;
using D3D9Format = Vortice.Direct3D9.Format;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class ImageSourceDirect2DResource : IDisposable
{
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDXGIDevice? _dxgiDevice;

    private IDirect3D9Ex? _d3d9;
    private IDirect3DDevice9Ex? _d3d9Device;

    private ID3D11Texture2D? _d3d11RenderTarget;
    private IDXGISurface? _dxgiSurface;
    private ID2D1Bitmap1? _targetBitmap;

    private IDirect3DTexture9? _sharedTexture9;
    private IDirect3DSurface9? _sharedSurface9;

    private IDWriteFactory? _dwriteFactory;
    private ID3D11ImageSource? _imageSource;

    private int _width;
    private int _height;

    private Vortice.Direct3D.FeatureLevel _featureLevel;
    private bool _usingWarp;
    private bool _disposed;
    private bool _isDrawing;

    public int Width => _width;
    public int Height => _height;
    public bool UsingWarp => _usingWarp;
    public Vortice.Direct3D.FeatureLevel FeatureLevel => _featureLevel;

    public IDWriteFactory? DwriteFactory => _dwriteFactory;

    public ID2D1DeviceContext? Context
    {
        get
        {
            ThrowIfDisposed();
            return _d2dContext;
        }
    }

    public ID2D1Factory1? Factory
    {
        get
        {
            ThrowIfDisposed();
            return _d2dFactory;
        }
    }

    public ID2D1Device? Device
    {
        get
        {
            ThrowIfDisposed();
            return _d2dDevice;
        }
    }

    public Direct2DResourceCache? Direct2DResourceCache { get; private set; }

    public bool IsTargetReady =>
        _imageSource != null &&
        _d3d11RenderTarget != null &&
        _targetBitmap != null &&
        _sharedSurface9 != null;

    public ImageSourceDirect2DResource()
    {
        CreateD2DFactory();
        CreateD3D11Device();
        CreateD2DDeviceAndContext();
        CreateD3D9Device();
        GetDWriteFactory();
    }

    public void SetTarget(ID3D11ImageSource imageSource)
    {
        ThrowIfDisposed();
        if (_imageSource == imageSource)
            return;
        _imageSource = imageSource ?? throw new ArgumentNullException(nameof(imageSource));

        var width = Math.Max(1, imageSource.SurfaceWidth);
        var height = Math.Max(1, imageSource.SurfaceHeight);

        SetSize(width, height);
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();

        if (_imageSource == null)
            throw new InvalidOperationException("ID3D11ImageSource is not set. Call SetTarget first.");

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_width == width && _height == height && IsTargetReady)
            return;

        if (_isDrawing)
            throw new InvalidOperationException("Cannot resize target between BeginDraw and EndDraw.");

        _width = width;
        _height = height;

        ReleaseImageTarget();

        CreateD3D11RenderTargetTexture();
        CreateD2DTargetBitmap();
        CreateD3D9SharedSurface();

        if (_d2dFactory is null || _dwriteFactory is null || _d2dContext is null)
            throw new InvalidOperationException("Failed to create necessary Direct2D resources.");

        Direct2DResourceCache ??= new Direct2DResourceCache(_d2dFactory, _dwriteFactory, _d2dContext);

        _d2dContext.Target = _targetBitmap;

        _imageSource.SetSurface(_sharedSurface9.NativePointer);
        _imageSource.Invalidate();
    }

    public void BeginDraw()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (_isDrawing)
            throw new InvalidOperationException("BeginDraw has already been called.");

        _d2dContext.BeginDraw();
        _isDrawing = true;
    }

    public void EndDraw(CadScreenRect? dirtyRect = null)
    {
        ThrowIfDisposed();

        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (!_isDrawing)
            throw new InvalidOperationException("BeginDraw has not been called.");

        try
        {
            _d2dContext.EndDraw();

            // 这里很重要：D2D 写入的是 D3D11 Texture，
            // 需要 Flush 后 WPF/D3D9 侧才能稳定看到内容。
            _d3dContext?.Flush();

            if (_imageSource is not null)
            {
                if (dirtyRect is { IsEmpty: false } rect)
                    _imageSource.Invalidate(new IntRect(rect.X, rect.Y, rect.Width, rect.Height));
                else
                    _imageSource.Invalidate();
            }
        }
        finally
        {
            _isDrawing = false;
        }
    }

    public void DrawFrame(Action<ID2D1DeviceContext> drawAction, CadScreenRect? dirtyRect = null)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));

        EnsureTargetReady();

        BeginDraw();
        drawAction(_d2dContext);
        EndDraw(dirtyRect);
    }

    public void Clear(float r, float g, float b, float a)
    {
        ThrowIfDisposed();

        if (!_isDrawing)
            throw new InvalidOperationException("Clear must be called between BeginDraw and EndDraw.");

        _d2dContext?.Clear(new Vortice.Mathematics.Color4(r, g, b, a));
    }

    [MemberNotNull(nameof(_d2dFactory))]
    private void CreateD2DFactory()
    {
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(
            Vortice.Direct2D1.FactoryType.MultiThreaded);
    }

    private IDWriteFactory GetDWriteFactory()
    {
        if (_dwriteFactory != null)
            return _dwriteFactory;

        _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        return _dwriteFactory;
    }

    private void CreateD3D11Device()
    {
        var featureLevels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0,
            Vortice.Direct3D.FeatureLevel.Level_9_3,
            Vortice.Direct3D.FeatureLevel.Level_9_2,
            Vortice.Direct3D.FeatureLevel.Level_9_1
        };

        if (TryCreateD3D11Device(DriverType.Hardware, featureLevels, false))
            return;

        if (TryCreateD3D11Device(DriverType.Warp, featureLevels, true))
            return;

        throw new InvalidOperationException("Failed to create D3D11 device.");
    }

    private bool TryCreateD3D11Device(
        DriverType driverType,
        Vortice.Direct3D.FeatureLevel[] featureLevels,
        bool usingWarp)
    {
        ID3D11Device? d3dDevice = null;
        ID3D11DeviceContext? d3dContext = null;

        try
        {
            var flags = DeviceCreationFlags.BgraSupport;

            var result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                flags,
                featureLevels,
                out d3dDevice,
                out _featureLevel,
                out d3dContext);

            if (result.Failure)
                return false;

            _d3dDevice = d3dDevice;
            _d3dContext = d3dContext;
            _usingWarp = usingWarp;
            _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();

            return true;
        }
        catch
        {
            d3dContext?.Dispose();
            d3dDevice?.Dispose();

            _dxgiDevice?.Dispose();
            _dxgiDevice = null;

            _d3dContext?.Dispose();
            _d3dContext = null;

            _d3dDevice?.Dispose();
            _d3dDevice = null;

            return false;
        }
    }

    private void CreateD2DDeviceAndContext()
    {
        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (_dxgiDevice is null)
            throw new InvalidOperationException("DXGI device is not created.");

        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);

        if (_d2dDevice is null)
            throw new InvalidOperationException("Failed to create Direct2D device.");

        _d2dContext = _d2dDevice.CreateDeviceContext(
            DeviceContextOptions.EnableMultithreadedOptimizations);

        if (_d2dContext is null)
            throw new InvalidOperationException("Failed to create Direct2D device context.");
    }

    private void CreateD3D9Device()
    {
        var presentParams = new Vortice.Direct3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
            DeviceWindowHandle = NativeMethods.GetDesktopWindow(),
            PresentationInterval = PresentInterval.Default
        };

        var createFlags =
            CreateFlags.HardwareVertexProcessing |
            CreateFlags.Multithreaded |
            CreateFlags.FpuPreserve;

        _d3d9 = D3D9Api.Direct3DCreate9Ex();

        _d3d9Device = _d3d9.CreateDeviceEx(
            0,
            DeviceType.Hardware,
            IntPtr.Zero,
            createFlags,
            presentParams);
    }

    [MemberNotNull(nameof(_d3dDevice), nameof(_d3d11RenderTarget), nameof(_dxgiSurface))]
    private void CreateD3D11RenderTargetTexture()
    {
        if (_d3dDevice is null)
            throw new InvalidOperationException("D3D11 device is not created.");

        var desc = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGIFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,

            // D3D11 -> D3D9Ex 共享必须要这个
            MiscFlags = ResourceOptionFlags.Shared
        };

        _d3d11RenderTarget = _d3dDevice.CreateTexture2D(desc);
        _dxgiSurface = _d3d11RenderTarget.QueryInterface<IDXGISurface>();
    }

    [MemberNotNull(nameof(_d2dContext), nameof(_dxgiSurface), nameof(_targetBitmap))]
    private void CreateD2DTargetBitmap()
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (_dxgiSurface is null)
            throw new InvalidOperationException("DXGI surface is not created.");

        var bitmapProperties = new BitmapProperties1
        {
            PixelFormat = new PixelFormat(
                DXGIFormat.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Ignore),
            DpiX = 96.0f,
            DpiY = 96.0f,
            BitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw
        };

        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(
            _dxgiSurface,
            bitmapProperties);
    }
    [MemberNotNull(nameof(_sharedSurface9))]
    private void CreateD3D9SharedSurface()
    {
        if (_d3d9Device is null)
            throw new InvalidOperationException("D3D9Ex device is not created.");

        if (_d3d11RenderTarget is null)
            throw new InvalidOperationException("D3D11 render target is not created.");

        var sharedHandle = GetSharedHandle(_d3d11RenderTarget);

        if (sharedHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to get shared handle from D3D11 texture.");

        _sharedTexture9 = _d3d9Device.CreateTexture(
            (uint)_width,
            (uint)_height,
            1,
            Vortice.Direct3D9.Usage.RenderTarget,
            D3D9Format.A8R8G8B8,
            Pool.Default,
            ref sharedHandle);

        _sharedSurface9 = _sharedTexture9.GetSurfaceLevel(0);
    }

    private static IntPtr GetSharedHandle(ID3D11Texture2D texture)
    {
        using var resource = texture.QueryInterface<IDXGIResource>();
        return resource.SharedHandle;
    }

    private void ReleaseImageTarget()
    {
        if (_d2dContext != null)
            _d2dContext.Target = null;

        _targetBitmap?.Dispose();
        _targetBitmap = null;

        _dxgiSurface?.Dispose();
        _dxgiSurface = null;

        _sharedSurface9?.Dispose();
        _sharedSurface9 = null;

        _sharedTexture9?.Dispose();
        _sharedTexture9 = null;

        _d3d11RenderTarget?.Dispose();
        _d3d11RenderTarget = null;
    }

    private void ReleaseDeviceResources()
    {
        Direct2DResourceCache?.ClearCache();

        _dwriteFactory?.Dispose();
        _dwriteFactory = null;

        _d2dContext?.Dispose();
        _d2dContext = null;

        _d2dDevice?.Dispose();
        _d2dDevice = null;

        _d2dFactory?.Dispose();
        _d2dFactory = null;

        _dxgiDevice?.Dispose();
        _dxgiDevice = null;

        _d3dContext?.Dispose();
        _d3dContext = null;

        _d3dDevice?.Dispose();
        _d3dDevice = null;

        _d3d9Device?.Dispose();
        _d3d9Device = null;

        _d3d9?.Dispose();
        _d3d9 = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isDrawing)
        {
            try
            {
                _d2dContext?.EndDraw();
            }
            catch
            {
                // ignored
            }
            finally
            {
                _isDrawing = false;
            }
        }

        try
        {
            _imageSource?.SetSurface(nint.Zero);
        }
        catch
        {
            // ignored
        }

        ReleaseImageTarget();
        ReleaseDeviceResources();

        _imageSource = null;
        _width = 0;
        _height = 0;
        _disposed = true;
    }

    [MemberNotNull(
        nameof(_imageSource),
        nameof(_d2dContext),
        nameof(_targetBitmap),
        nameof(_sharedSurface9))]
    private void EnsureTargetReady()
    {
        if (_imageSource == null)
            throw new InvalidOperationException("ID3D11ImageSource is not set. Call SetTarget first.");

        if (_d2dContext == null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (_targetBitmap == null)
            throw new InvalidOperationException("Direct2D target bitmap is not created.");

        if (_sharedSurface9 == null)
            throw new InvalidOperationException("D3D9 shared surface is not created.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ImageSourceDirect2DResource));
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetDesktopWindow();
    }
}
