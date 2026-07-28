using System.Diagnostics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DxgiFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

/// <summary>
/// Draws ordered entity chunks on independent D3D/D2D devices and composites their shared
/// textures on the main device. Each slot owns a complete device-bound scene resource cache.
/// </summary>
internal sealed class Direct2DMultiDeviceSceneRenderer : IDisposable
{
    internal const int MaximumDeviceCount = 4;
    internal const int DefaultDeviceCount = 2;
    internal const int DefaultEntityThreshold = 1000;

    private WorkerSlot[]? _slots;
    private CadDocument? _document;
    private nint _mainDevicePointer;
    private int _width;
    private int _height;
    private int _deviceCount;
    private bool _poolCreationFailed;
    private bool _disposed;

    public bool TryDraw(
        ID3D11Device mainD3DDevice,
        ID2D1DeviceContext mainD2DContext,
        IDWriteFactory dwriteFactory,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> entities,
        int width,
        int height,
        Action beforeComposite,
        out Direct2DMultiDeviceFrameLease? frameLease,
        out Direct2DMultiDeviceFrameMetrics metrics)
    {
        ThrowIfDisposed();
        frameLease = null;
        metrics = default;

        var requestedDeviceCount = Math.Clamp(
            options.MultiDeviceRenderingDeviceCount,
            2,
            MaximumDeviceCount);
        var threshold = Math.Max(2, options.MultiDeviceRenderingEntityThreshold);
        if (!options.IsMultiDeviceRenderingEnabled ||
            options.ActiveLayoutId is not null ||
            entities.Count < threshold ||
            width <= 0 ||
            height <= 0 ||
            requestedDeviceCount <= 1 ||
            ContainsUnsupportedEntities(entities))
        {
            if (!options.IsMultiDeviceRenderingEnabled)
                Reset();
            return false;
        }

        var activeDeviceCount = Math.Min(requestedDeviceCount, entities.Count);
        var batches = CreateBatches(entities, activeDeviceCount);
        var started = Stopwatch.GetTimestamp();
        CadRenderStatistics[] workerStatistics;
        try
        {
            if (!EnsurePool(
                    mainD3DDevice,
                    mainD2DContext,
                    dwriteFactory,
                    document,
                    width,
                    height,
                    activeDeviceCount))
            {
                return false;
            }

            workerStatistics = DrawOnWorkers(document, viewport, options, batches);
        }
        catch (Exception exception)
        {
            // A worker-only failure must not take down the foreground renderer.
            // No worker texture has been submitted to the main context at this point.
            Debug.WriteLine(exception);
            Reset();
            return false;
        }

        try
        {
            beforeComposite();
            frameLease = Composite(mainD2DContext, batches);
            metrics = new Direct2DMultiDeviceFrameMetrics(
                activeDeviceCount,
                entities.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                workerStatistics);
            return true;
        }
        catch
        {
            Reset();
            throw;
        }
    }

    private bool EnsurePool(
        ID3D11Device mainD3DDevice,
        ID2D1DeviceContext mainD2DContext,
        IDWriteFactory dwriteFactory,
        CadDocument document,
        int width,
        int height,
        int deviceCount)
    {
        if (_poolCreationFailed)
            return false;
        if (_slots is not null &&
            ReferenceEquals(_document, document) &&
            _mainDevicePointer == mainD3DDevice.NativePointer &&
            _width == width &&
            _height == height &&
            _deviceCount == deviceCount)
        {
            return true;
        }

        Reset();
        var newSlots = new WorkerSlot[deviceCount];
        using var mainDxgiDevice = mainD3DDevice.QueryInterface<IDXGIDevice>();
        using var adapter = mainDxgiDevice.GetAdapter();
        try
        {
            for (var index = 0; index < newSlots.Length; index++)
            {
                newSlots[index] = WorkerSlot.Create(
                    adapter,
                    mainD3DDevice,
                    mainD2DContext,
                    dwriteFactory,
                    document,
                    width,
                    height);
            }

            _slots = newSlots;
            _document = document;
            _mainDevicePointer = mainD3DDevice.NativePointer;
            _width = width;
            _height = height;
            _deviceCount = deviceCount;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            foreach (var slot in newSlots)
            {
                try
                {
                    slot?.Dispose();
                }
                catch
                {
                    // Optimization cleanup must not hide the original initialization failure.
                }
            }

            _poolCreationFailed = true;
            return false;
        }
    }

    private CadRenderStatistics[] DrawOnWorkers(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        RenderBatch[] batches)
    {
        var completed = new int[batches.Length];
        var workerStatistics = new CadRenderStatistics[batches.Length];
        try
        {
            Parallel.For(
                0,
                batches.Length,
                new ParallelOptions { MaxDegreeOfParallelism = batches.Length },
                index =>
                {
                    workerStatistics[index] = _slots![index].Draw(
                        document,
                        viewport,
                        options,
                        batches[index].Entities);
                    Volatile.Write(ref completed[index], 1);
                });
            return workerStatistics;
        }
        catch
        {
            for (var index = 0; index < batches.Length; index++)
            {
                if (Volatile.Read(ref completed[index]) == 0)
                    continue;
                try
                {
                    _slots![index].ReleaseUnreadFrame();
                }
                catch
                {
                    // Device-loss handling will rebuild the pool.
                }
            }

            throw;
        }
    }

    private Direct2DMultiDeviceFrameLease Composite(
        ID2D1DeviceContext mainContext,
        IReadOnlyList<RenderBatch> batches)
    {
        var lease = new Direct2DMultiDeviceFrameLease(batches.Count);
        var acquired = new bool[batches.Count];
        try
        {
            for (var index = 0; index < batches.Count; index++)
            {
                var slot = _slots![index];
                slot.AcquireForMainRead();
                acquired[index] = true;
                lease.Add(slot);
                mainContext.DrawImage(
                    slot.MainReadableBitmap,
                    null,
                    null,
                    Vortice.Direct2D1.InterpolationMode.NearestNeighbor,
                    CompositeMode.SourceOver);
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            for (var index = 0; index < batches.Count; index++)
            {
                if (acquired[index])
                    continue;
                try
                {
                    _slots![index].ReleaseUnreadFrame();
                }
                catch
                {
                    // Device-loss handling will rebuild the pool.
                }
            }

            throw;
        }
    }

    private static RenderBatch[] CreateBatches(
        IReadOnlyList<CadEntity> entities,
        int deviceCount)
    {
        var baseChunkSize = entities.Count / deviceCount;
        var remainder = entities.Count % deviceCount;
        var result = new RenderBatch[deviceCount];
        for (var index = 0; index < deviceCount; index++)
        {
            var start = index * baseChunkSize + Math.Min(index, remainder);
            var count = baseChunkSize + (index < remainder ? 1 : 0);
            var chunk = new CadEntity[count];
            for (var entityIndex = 0; entityIndex < count; entityIndex++)
                chunk[entityIndex] = entities[start + entityIndex];
            result[index] = new RenderBatch(chunk);
        }

        return result;
    }

    private static bool ContainsUnsupportedEntities(IReadOnlyList<CadEntity> entities)
    {
        foreach (var entity in entities)
        {
            // OLE drawing can call UI/COM callbacks, while block references own nested caches and
            // may contain OLE entities. Preserve the foreground renderer for either case.
            if (entity is CadOleObject or CadBlockReference)
                return true;
        }

        return false;
    }

    public void Reset()
    {
        if (_slots is not null)
        {
            foreach (var slot in _slots)
            {
                try
                {
                    slot.Dispose();
                }
                catch
                {
                    // Reset is also used during device loss.
                }
            }
        }

        _slots = null;
        _document = null;
        _mainDevicePointer = 0;
        _width = 0;
        _height = 0;
        _deviceCount = 0;
        _poolCreationFailed = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Reset();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DMultiDeviceSceneRenderer));
    }

    private sealed record RenderBatch(IReadOnlyList<CadEntity> Entities);

    internal sealed class WorkerSlot : IDisposable
    {
        private readonly object _useGate = new();
        private readonly ID3D11Device _d3dDevice;
        private readonly ID3D11DeviceContext _d3dContext;
        private readonly IDXGIDevice _dxgiDevice;
        private readonly ID2D1Factory1 _d2dFactory;
        private readonly ID2D1Device _d2dDevice;
        private readonly ID2D1DeviceContext _d2dContext;
        private readonly Direct2DSceneRender _renderer;
        private readonly ID3D11Texture2D _workerTexture;
        private readonly IDXGIKeyedMutex _workerMutex;
        private readonly ID2D1Bitmap1 _workerTarget;
        private readonly ID3D11Texture2D _mainTexture;
        private readonly IDXGIKeyedMutex _mainMutex;

        private WorkerSlot(
            ID3D11Device d3dDevice,
            ID3D11DeviceContext d3dContext,
            IDXGIDevice dxgiDevice,
            ID2D1Factory1 d2dFactory,
            ID2D1Device d2dDevice,
            ID2D1DeviceContext d2dContext,
            Direct2DSceneRender renderer,
            ID3D11Texture2D workerTexture,
            IDXGIKeyedMutex workerMutex,
            ID2D1Bitmap1 workerTarget,
            ID3D11Texture2D mainTexture,
            IDXGIKeyedMutex mainMutex,
            ID2D1Bitmap1 mainReadableBitmap)
        {
            _d3dDevice = d3dDevice;
            _d3dContext = d3dContext;
            _dxgiDevice = dxgiDevice;
            _d2dFactory = d2dFactory;
            _d2dDevice = d2dDevice;
            _d2dContext = d2dContext;
            _renderer = renderer;
            _workerTexture = workerTexture;
            _workerMutex = workerMutex;
            _workerTarget = workerTarget;
            _mainTexture = mainTexture;
            _mainMutex = mainMutex;
            MainReadableBitmap = mainReadableBitmap;
        }

        public ID2D1Bitmap1 MainReadableBitmap { get; }

        public static WorkerSlot Create(
            IDXGIAdapter adapter,
            ID3D11Device mainD3DDevice,
            ID2D1DeviceContext mainD2DContext,
            IDWriteFactory dwriteFactory,
            CadDocument document,
            int width,
            int height)
        {
            ID3D11Device? d3dDevice = null;
            ID3D11DeviceContext? d3dContext = null;
            IDXGIDevice? dxgiDevice = null;
            ID2D1Factory1? d2dFactory = null;
            ID2D1Device? d2dDevice = null;
            ID2D1DeviceContext? d2dContext = null;
            Direct2DSceneRender? renderer = null;
            ID3D11Texture2D? workerTexture = null;
            IDXGIKeyedMutex? workerMutex = null;
            ID2D1Bitmap1? workerTarget = null;
            ID3D11Texture2D? mainTexture = null;
            IDXGIKeyedMutex? mainMutex = null;
            ID2D1Bitmap1? mainReadableBitmap = null;
            try
            {
                D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport,
                    [
                        Vortice.Direct3D.FeatureLevel.Level_11_1,
                        Vortice.Direct3D.FeatureLevel.Level_11_0,
                        Vortice.Direct3D.FeatureLevel.Level_10_1,
                        Vortice.Direct3D.FeatureLevel.Level_10_0
                    ],
                    out ID3D11Device createdDevice,
                    out _,
                    out ID3D11DeviceContext createdContext).CheckError();
                d3dDevice = createdDevice;
                d3dContext = createdContext;

                workerTexture = d3dDevice.CreateTexture2D(
                    CreateSharedTextureDescription(width, height));
                workerMutex = workerTexture.QueryInterface<IDXGIKeyedMutex>();
                nint sharedHandle;
                using (var resource = workerTexture.QueryInterface<IDXGIResource>())
                    sharedHandle = resource.SharedHandle;

                dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
                d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(
                    D2DFactoryType.SingleThreaded);
                d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
                d2dContext = d2dDevice.CreateDeviceContext(
                    DeviceContextOptions.EnableMultithreadedOptimizations);
                workerTarget = CreateWorkerTarget(d2dContext, workerTexture);
                d2dContext.Target = workerTarget;

                mainTexture =
                    mainD3DDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
                mainMutex = mainTexture.QueryInterface<IDXGIKeyedMutex>();
                mainReadableBitmap = CreateMainBitmap(mainD2DContext, mainTexture);

                renderer = new Direct2DSceneRender();
                renderer.ResetDeviceResources(
                    d2dFactory,
                    dwriteFactory,
                    d2dDevice,
                    d2dContext,
                    document);

                return new WorkerSlot(
                    d3dDevice,
                    d3dContext,
                    dxgiDevice,
                    d2dFactory,
                    d2dDevice,
                    d2dContext,
                    renderer,
                    workerTexture,
                    workerMutex,
                    workerTarget,
                    mainTexture,
                    mainMutex,
                    mainReadableBitmap);
            }
            catch
            {
                try { renderer?.Dispose(); } catch { }
                try { if (d2dContext is not null) d2dContext.Target = null; } catch { }
                try { mainReadableBitmap?.Dispose(); } catch { }
                try { mainMutex?.Dispose(); } catch { }
                try { mainTexture?.Dispose(); } catch { }
                try { workerTarget?.Dispose(); } catch { }
                try { workerMutex?.Dispose(); } catch { }
                try { workerTexture?.Dispose(); } catch { }
                try { d2dContext?.Dispose(); } catch { }
                try { d2dDevice?.Dispose(); } catch { }
                try { d2dFactory?.Dispose(); } catch { }
                try { dxgiDevice?.Dispose(); } catch { }
                try { d3dContext?.ClearState(); } catch { }
                try { d3dContext?.Dispose(); } catch { }
                try { d3dDevice?.Dispose(); } catch { }
                throw;
            }
        }

        public CadRenderStatistics Draw(
            CadDocument document,
            CadViewport viewport,
            CadRenderOptions options,
            IReadOnlyList<CadEntity> entities)
        {
            lock (_useGate)
            {
                var mutexAcquired = false;
                var drawBegun = false;
                var frameBegun = false;
                var readyForMain = false;
                try
                {
                    _workerMutex.AcquireSync(0, int.MaxValue);
                    mutexAcquired = true;
                    _renderer.BeginFrame();
                    frameBegun = true;
                    _d2dContext.BeginDraw();
                    drawBegun = true;
                    _d2dContext.Transform = System.Numerics.Matrix3x2.Identity;
                    _d2dContext.Clear(new Color4(0, 0, 0, 0));
                    _renderer.RenderEntityBatch(
                        document,
                        viewport,
                        options,
                        entities);
                    _d2dContext.EndDraw().CheckError();
                    drawBegun = false;
                    _renderer.CompleteFrame();
                    frameBegun = false;
                    readyForMain = true;
                    return _renderer.RenderStatistics;
                }
                finally
                {
                    if (drawBegun)
                    {
                        try { _d2dContext.EndDraw(); } catch { }
                    }
                    if (frameBegun)
                    {
                        try { _renderer.CompleteFrame(); } catch { }
                    }
                    if (mutexAcquired)
                        _workerMutex.ReleaseSync(readyForMain ? 1u : 0u);
                }
            }
        }

        public void AcquireForMainRead() =>
            _mainMutex.AcquireSync(1, int.MaxValue);

        public void ReleaseToWorker() => _mainMutex.ReleaseSync(0);

        public void ReleaseUnreadFrame()
        {
            _mainMutex.AcquireSync(1, 0);
            _mainMutex.ReleaseSync(0);
        }

        public void Dispose()
        {
            lock (_useGate)
            {
                try { _renderer.Dispose(); } catch { }
                try { _d2dContext.Target = null; } catch { }
                MainReadableBitmap.Dispose();
                _mainMutex.Dispose();
                _mainTexture.Dispose();
                _workerTarget.Dispose();
                _workerMutex.Dispose();
                _workerTexture.Dispose();
                _d2dContext.Dispose();
                _d2dDevice.Dispose();
                _d2dFactory.Dispose();
                _dxgiDevice.Dispose();
                try { _d3dContext.ClearState(); } catch { }
                _d3dContext.Dispose();
                _d3dDevice.Dispose();
            }
        }

        private static Texture2DDescription CreateSharedTextureDescription(
            int width,
            int height) => new()
            {
                Width = (uint)Math.Max(1, width),
                Height = (uint)Math.Max(1, height),
                MipLevels = 1,
                ArraySize = 1,
                Format = DxgiFormat.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.SharedKeyedMutex
            };

        private static ID2D1Bitmap1 CreateWorkerTarget(
            ID2D1DeviceContext context,
            ID3D11Texture2D texture)
        {
            using var surface = texture.QueryInterface<IDXGISurface>();
            return context.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DxgiFormat.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96,
                    DpiY = 96,
                    BitmapOptions = BitmapOptions.Target
                });
        }

        private static ID2D1Bitmap1 CreateMainBitmap(
            ID2D1DeviceContext context,
            ID3D11Texture2D texture)
        {
            using var surface = texture.QueryInterface<IDXGISurface>();
            return context.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DxgiFormat.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96,
                    DpiY = 96,
                    BitmapOptions = BitmapOptions.None
                });
        }
    }
}

internal readonly record struct Direct2DMultiDeviceFrameMetrics(
    int WorkerCount,
    int EntityCount,
    double ElapsedMilliseconds,
    IReadOnlyList<CadRenderStatistics> WorkerStatistics);

internal sealed class Direct2DMultiDeviceFrameLease : IDisposable
{
    private readonly Direct2DMultiDeviceSceneRenderer.WorkerSlot[] _slots;
    private int _count;
    private bool _disposed;

    public Direct2DMultiDeviceFrameLease(int capacity)
    {
        _slots =
            new Direct2DMultiDeviceSceneRenderer.WorkerSlot[Math.Max(0, capacity)];
    }

    public void Add(Direct2DMultiDeviceSceneRenderer.WorkerSlot slot)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DMultiDeviceFrameLease));
        _slots[_count++] = slot;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        for (var index = 0; index < _count; index++)
        {
            try
            {
                _slots[index].ReleaseToWorker();
            }
            catch
            {
                // Device recovery owns the worker pool after a release failure.
            }
        }
        _disposed = true;
    }
}
