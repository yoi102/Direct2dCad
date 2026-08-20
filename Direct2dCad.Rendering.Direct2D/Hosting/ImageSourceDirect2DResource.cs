using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Direct2dCad.Rendering;

using D3D9Api = Vortice.Direct3D9.D3D9;
using D3D9Format = Vortice.Direct3D9.Format;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

internal sealed class ImageSourceDirect2DResource : IDisposable
{
    private readonly List<IntRect> _presentDirtyRects = new(8);
    private readonly CadScreenRect[] _singleDirtyRect = new CadScreenRect[1];
    private readonly Action _presentBackBuffer;
    private readonly Func<ID2D1DeviceContext, Result>? _endDrawOverride;
    private readonly Action? _beforeDeviceResourcesReleased;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private IDXGIDevice? _dxgiDevice;

    private IDirect3D9Ex? _d3d9;
    private IDirect3DDevice9Ex? _d3d9Device;

    private ID3D11Texture2D? _d3d11RenderTarget;
    private ID3D11Texture2D? _d3d11BackBuffer;
    private IDXGISurface? _dxgiSurface;
    private ID2D1Bitmap1? _targetBitmap;
    private ID3D11Texture2D? _baseSceneTexture;
    private ID3D11Texture2D? _interactionSnapshotTexture;
    private IDXGISurface? _interactionSnapshotSurface;
    private ID2D1Bitmap1? _interactionSnapshotBitmap;

    private IDirect3DTexture9? _sharedTexture9;
    private IDirect3DSurface9? _sharedSurface9;

    private IDWriteFactory? _dwriteFactory;
    private ID3D11ImageSource? _imageSource;

    private int _width;
    private int _height;

    private Vortice.Direct3D.FeatureLevel _featureLevel;
    private bool _usingWarp;
    private CadGraphicsDeviceMode _graphicsDeviceMode = CadGraphicsDeviceMode.Automatic;
    private bool _disposed;
    private bool _isDrawing;
    private IReadOnlyList<CadScreenRect>? _pendingPresentDirtyRects;

    public int Width => _width;
    public int Height => _height;
    public bool UsingWarp => _usingWarp;
    public CadGraphicsDeviceMode GraphicsDeviceMode => _graphicsDeviceMode;
    public Vortice.Direct3D.FeatureLevel FeatureLevel => _featureLevel;

    public IDWriteFactory? DwriteFactory => _dwriteFactory;

    public ID3D11Device? D3DDevice
    {
        get
        {
            ThrowIfDisposed();
            return _d3dDevice;
        }
    }

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

    public bool IsTargetReady =>
        _imageSource != null &&
        _d3d11RenderTarget != null &&
        _d3d11BackBuffer != null &&
        _targetBitmap != null &&
        _sharedSurface9 != null;

    public bool HasBaseSceneSnapshot => _baseSceneTexture is not null;

    public ImageSourceDirect2DResource(
        Func<ID2D1DeviceContext, Result>? endDrawOverride = null,
        Action? beforeDeviceResourcesReleased = null)
    {
        _endDrawOverride = endDrawOverride;
        _beforeDeviceResourcesReleased = beforeDeviceResourcesReleased;
        _presentBackBuffer = PresentBackBuffer;
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

    public void SetGraphicsDeviceMode(CadGraphicsDeviceMode mode)
    {
        ThrowIfDisposed();

        if (!Enum.IsDefined(mode))
            mode = CadGraphicsDeviceMode.Automatic;

        if (_graphicsDeviceMode == mode)
            return;

        _graphicsDeviceMode = mode;
        RecoverFromDeviceLoss();
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

        _d2dContext.Target = _targetBitmap;

        _imageSource!.SetSurface(_sharedSurface9.NativePointer);
        _imageSource!.Invalidate();
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

    public void EndDraw(CadScreenRect? dirtyRect = null, bool present = true)
    {
        if (dirtyRect is not { IsEmpty: false } rect)
        {
            EndDraw((IReadOnlyList<CadScreenRect>?)null, present);
            return;
        }

        _singleDirtyRect[0] = rect;
        EndDraw(_singleDirtyRect, present);
    }

    public void EndDraw(
        IReadOnlyList<CadScreenRect>? dirtyRects,
        bool present = true)
    {
        ThrowIfDisposed();

        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (!_isDrawing)
            throw new InvalidOperationException("BeginDraw has not been called.");

        try
        {
            var result = _endDrawOverride is null
                ? _d2dContext.EndDraw()
                : _endDrawOverride(_d2dContext);
            _isDrawing = false;

            if (result.Failure)
            {
                if (Direct2DDeviceFailureClassifier.IsRecoverable(result))
                {
                    RecoverFromDeviceLoss();
                    throw new Direct2DDeviceResourcesRecreatedException(result);
                }

                result.CheckError();
            }

            if (!present)
                return;

            _pendingPresentDirtyRects = dirtyRects;
            try
            {
                if (_imageSource is not null)
                    _imageSource.Present(
                        _presentBackBuffer,
                        dirtyRects is { Count: > 0 }
                            ? PrepareIntRects(dirtyRects)
                            : null);
                else
                    PresentBackBuffer();
            }
            finally
            {
                _pendingPresentDirtyRects = null;
            }
        }
        finally
        {
            _isDrawing = false;
        }
    }

    public void DrawFrame(
        Action<ID2D1DeviceContext> drawAction,
        CadScreenRect? dirtyRect = null,
        bool present = true)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));

        EnsureTargetReady();

        BeginDraw();
        drawAction(_d2dContext);
        EndDraw(dirtyRect, present);
    }

    public void DrawFrame(
        Action<ID2D1DeviceContext> drawAction,
        IReadOnlyList<CadScreenRect>? dirtyRects,
        bool present = true)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));

        EnsureTargetReady();

        BeginDraw();
        drawAction(_d2dContext);
        EndDraw(dirtyRects, present);
    }

    public bool CaptureBaseScene(IReadOnlyList<CadScreenRect>? dirtyRects)
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        if (_isDrawing || _d3dDevice is null || _d3dContext is null)
            return false;

        try
        {
            EnsureBaseSceneResources();
            CopyTextureRegions(
                _d3d11BackBuffer!,
                _baseSceneTexture,
                dirtyRects);
            return true;
        }
        catch
        {
            ReleaseBaseScene();
            return false;
        }
    }

    public bool RestoreBaseScene(IReadOnlyList<CadScreenRect>? dirtyRects)
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        if (_isDrawing || _d3dContext is null || _baseSceneTexture is null)
            return false;

        try
        {
            CopyTextureRegions(
                _baseSceneTexture,
                _d3d11BackBuffer!,
                dirtyRects);
            return true;
        }
        catch
        {
            ReleaseBaseScene();
            return false;
        }
    }

    [MemberNotNull(nameof(_baseSceneTexture))]
    private void EnsureBaseSceneResources()
    {
        if (_baseSceneTexture is not null)
            return;
        if (_d3dDevice is null)
            throw new InvalidOperationException("Direct3D device resources are not ready.");

        _baseSceneTexture = _d3dDevice.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGIFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
    }

    private void CopyTextureRegions(
        ID3D11Texture2D source,
        ID3D11Texture2D destination,
        IReadOnlyList<CadScreenRect>? dirtyRects)
    {
        if (_d3dContext is null)
            throw new InvalidOperationException("Direct3D device context is not ready.");

        if (dirtyRects is not { Count: > 0 })
        {
            _d3dContext.CopyResource(destination, source);
            _d3dContext.Flush();
            return;
        }

        var copiedAnyRegion = false;
        foreach (var dirtyRect in dirtyRects)
        {
            var left = Math.Clamp(dirtyRect.X, 0, _width);
            var top = Math.Clamp(dirtyRect.Y, 0, _height);
            var right = (int)Math.Clamp(
                (long)dirtyRect.X + dirtyRect.Width,
                left,
                _width);
            var bottom = (int)Math.Clamp(
                (long)dirtyRect.Y + dirtyRect.Height,
                top,
                _height);
            if (right <= left || bottom <= top)
                continue;

            _d3dContext.CopySubresourceRegion(
                destination,
                0,
                (uint)left,
                (uint)top,
                0,
                source,
                0,
                new Box(left, top, 0, right, bottom, 1));
            copiedAnyRegion = true;
        }

        if (copiedAnyRegion)
            _d3dContext.Flush();
    }

    public bool CaptureFrameSnapshot()
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        if (_isDrawing || _d3dDevice is null || _d3dContext is null || _d2dContext is null)
            return false;

        try
        {
            EnsureFrameSnapshotResources();
            _d3dContext.CopyResource(_interactionSnapshotTexture, _d3d11BackBuffer);
            _d3dContext.Flush();
            return true;
        }
        catch
        {
            ReleaseFrameSnapshot();
            return false;
        }
    }

    internal byte[] CaptureBackBufferPixels()
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        if (_isDrawing ||
            _d3dDevice is null ||
            _d3dContext is null ||
            _d3d11BackBuffer is null)
        {
            return [];
        }

        return CaptureTexturePixels(_d3d11BackBuffer);
    }

    internal byte[] CapturePresentedPixels()
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        if (_isDrawing ||
            _d3dDevice is null ||
            _d3dContext is null ||
            _d3d11RenderTarget is null)
        {
            return [];
        }

        return CaptureTexturePixels(_d3d11RenderTarget);
    }

    private byte[] CaptureTexturePixels(ID3D11Texture2D source)
    {
        if (_d3dDevice is null || _d3dContext is null)
            return [];

        using var staging = _d3dDevice.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGIFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });
        _d3dContext.CopyResource(staging, source);
        _d3dContext.Flush();

        _d3dContext.Map(
            staging,
            0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None,
            out var mapped).CheckError();
        try
        {
            var rowBytes = checked(_width * 4);
            var pixels = new byte[checked(rowBytes * _height)];
            for (var row = 0; row < _height; row++)
            {
                Marshal.Copy(
                    IntPtr.Add(mapped.DataPointer, checked((int)(row * mapped.RowPitch))),
                    pixels,
                    row * rowBytes,
                    rowBytes);
            }

            return pixels;
        }
        finally
        {
            _d3dContext.Unmap(staging, 0);
        }
    }

    public bool DrawFrameSnapshot(
        System.Numerics.Matrix3x2 screenTransform,
        Color4 background,
        Vortice.Direct2D1.InterpolationMode interpolationMode,
        Action<ID2D1DeviceContext>? drawAfterSnapshot = null)
    {
        ThrowIfDisposed();
        if (_interactionSnapshotBitmap is null || !IsTargetReady)
            return false;

        DrawFrame(context =>
        {
            var previousTransform = context.Transform;
            var previousAntialiasMode = context.AntialiasMode;
            try
            {
                context.Transform = System.Numerics.Matrix3x2.Identity;
                context.Clear(background);
                context.Transform = screenTransform;
                context.AntialiasMode = AntialiasMode.Aliased;
                context.DrawBitmap(
                    _interactionSnapshotBitmap,
                    new RawRectF(0, 0, _width, _height),
                    1.0f,
                    interpolationMode,
                    null,
                    null);
            }
            finally
            {
                context.AntialiasMode = previousAntialiasMode;
                context.Transform = previousTransform;
            }

            drawAfterSnapshot?.Invoke(context);
        });
        return true;
    }

    [MemberNotNull(
        nameof(_interactionSnapshotTexture),
        nameof(_interactionSnapshotSurface),
        nameof(_interactionSnapshotBitmap))]
    private void EnsureFrameSnapshotResources()
    {
        if (_interactionSnapshotTexture is not null &&
            _interactionSnapshotSurface is not null &&
            _interactionSnapshotBitmap is not null)
        {
            return;
        }

        if (_d3dDevice is null || _d2dContext is null)
            throw new InvalidOperationException("Direct2D device resources are not ready.");

        ReleaseFrameSnapshot();
        try
        {
            var description = new Texture2DDescription
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
                MiscFlags = ResourceOptionFlags.None
            };
            _interactionSnapshotTexture = _d3dDevice.CreateTexture2D(description);
            _interactionSnapshotSurface =
                _interactionSnapshotTexture.QueryInterface<IDXGISurface>();
            _interactionSnapshotBitmap = _d2dContext.CreateBitmapFromDxgiSurface(
                _interactionSnapshotSurface,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DXGIFormat.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Ignore),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.None
                });
        }
        catch
        {
            ReleaseFrameSnapshot();
            throw;
        }
    }

    public void ReleaseFrameSnapshot()
    {
        _interactionSnapshotBitmap?.Dispose();
        _interactionSnapshotBitmap = null;

        _interactionSnapshotSurface?.Dispose();
        _interactionSnapshotSurface = null;

        _interactionSnapshotTexture?.Dispose();
        _interactionSnapshotTexture = null;
    }

    public void ReleaseBaseScene()
    {
        _baseSceneTexture?.Dispose();
        _baseSceneTexture = null;
    }

    private IReadOnlyList<IntRect> PrepareIntRects(IReadOnlyList<CadScreenRect> dirtyRects)
    {
        _presentDirtyRects.Clear();
        if (_presentDirtyRects.Capacity < dirtyRects.Count)
            _presentDirtyRects.Capacity = dirtyRects.Count;
        for (var i = 0; i < dirtyRects.Count; i++)
        {
            var rect = dirtyRects[i];
            _presentDirtyRects.Add(new IntRect(rect.X, rect.Y, rect.Width, rect.Height));
        }

        return _presentDirtyRects;
    }

    private void PresentBackBuffer()
    {
        if (_d3dContext is null || _d3d11RenderTarget is null || _d3d11BackBuffer is null)
            return;

        if (_pendingPresentDirtyRects is { Count: > 0 } dirtyRects)
        {
            var copiedAnyRegion = false;
            foreach (var dirtyRect in dirtyRects)
            {
                var left = Math.Clamp(dirtyRect.X, 0, _width);
                var top = Math.Clamp(dirtyRect.Y, 0, _height);
                var right = (int)Math.Clamp(
                    (long)dirtyRect.X + dirtyRect.Width,
                    left,
                    _width);
                var bottom = (int)Math.Clamp(
                    (long)dirtyRect.Y + dirtyRect.Height,
                    top,
                    _height);
                if (right <= left || bottom <= top)
                    continue;

                _d3dContext.CopySubresourceRegion(
                    _d3d11RenderTarget,
                    0,
                    (uint)left,
                    (uint)top,
                    0,
                    _d3d11BackBuffer,
                    0,
                    new Box(left, top, 0, right, bottom, 1));
                copiedAnyRegion = true;
            }

            if (copiedAnyRegion)
                _d3dContext.Flush();
            return;
        }

        _d3dContext.CopyResource(_d3d11RenderTarget, _d3d11BackBuffer);
        _d3dContext.Flush();
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

        switch (_graphicsDeviceMode)
        {
            case CadGraphicsDeviceMode.Hardware:
                if (TryCreateD3D11Device(DriverType.Hardware, featureLevels, false))
                    return;
                break;

            case CadGraphicsDeviceMode.Warp:
                if (TryCreateD3D11Device(DriverType.Warp, featureLevels, true))
                    return;
                break;

            default:
                if (TryCreateD3D11Device(DriverType.Hardware, featureLevels, false))
                    return;

                if (TryCreateD3D11Device(DriverType.Warp, featureLevels, true))
                    return;
                break;
        }

        throw new InvalidOperationException(
            $"Failed to create D3D11 device using {_graphicsDeviceMode} mode.");
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

    [MemberNotNull(
        nameof(_d3dDevice),
        nameof(_d3d11RenderTarget),
        nameof(_d3d11BackBuffer),
        nameof(_dxgiSurface))]
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

        desc.BindFlags = BindFlags.RenderTarget;
        desc.MiscFlags = ResourceOptionFlags.None;
        _d3d11BackBuffer = _d3dDevice.CreateTexture2D(desc);
        _dxgiSurface = _d3d11BackBuffer.QueryInterface<IDXGISurface>();
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

    internal void RecoverFromDeviceLoss()
    {
        ThrowIfDisposed();

        var width = Math.Max(1, _width);
        var height = Math.Max(1, _height);
        var hasImageSource = _imageSource is not null;

        if (hasImageSource)
        {
            try
            {
                _imageSource!.SetSurface(nint.Zero);
            }
            catch
            {
                // The old shared surface may already be invalid when the device is lost.
            }
        }

        _beforeDeviceResourcesReleased?.Invoke();
        ReleaseImageTarget();
        ReleaseDeviceResources();

        CreateD2DFactory();
        CreateD3D11Device();
        CreateD2DDeviceAndContext();
        CreateD3D9Device();
        GetDWriteFactory();

        if (!hasImageSource)
            return;

        _width = width;
        _height = height;

        CreateD3D11RenderTargetTexture();
        CreateD2DTargetBitmap();
        CreateD3D9SharedSurface();

        if (_d2dFactory is null || _dwriteFactory is null || _d2dContext is null)
            throw new InvalidOperationException("Failed to recreate Direct2D device resources.");

        _d2dContext.Target = _targetBitmap;
        _imageSource!.SetSurface(_sharedSurface9.NativePointer);
        _imageSource!.Invalidate();
    }

    private void ReleaseImageTarget()
    {
        ReleaseBaseScene();
        ReleaseFrameSnapshot();

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

        _d3d11BackBuffer?.Dispose();
        _d3d11BackBuffer = null;
    }

    private void ReleaseDeviceResources()
    {
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
        nameof(_d3d11RenderTarget),
        nameof(_d3d11BackBuffer),
        nameof(_targetBitmap),
        nameof(_sharedSurface9))]
    private void EnsureTargetReady()
    {
        if (_imageSource == null)
            throw new InvalidOperationException("ID3D11ImageSource is not set. Call SetTarget first.");

        if (_d2dContext == null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (_d3d11RenderTarget == null)
            throw new InvalidOperationException("D3D11 shared render target is not created.");

        if (_d3d11BackBuffer == null)
            throw new InvalidOperationException("D3D11 back buffer is not created.");

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

internal sealed class Direct2DDeviceResourcesRecreatedException : Exception
{
    public Direct2DDeviceResourcesRecreatedException(Result result)
        : base($"Direct2D device resources were recreated after EndDraw failed with {result}.")
    {
        Result = result;
    }

    public Result Result { get; }
}
