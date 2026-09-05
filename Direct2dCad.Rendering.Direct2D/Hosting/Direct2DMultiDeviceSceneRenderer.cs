using System.Diagnostics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

/// <summary>
/// Draws ordered entity chunks on independent D3D/D2D devices and composites their shared
/// textures on the main device. Each slot retains resources for the entities it has drawn.
/// </summary>
internal sealed class Direct2DMultiDeviceSceneRenderer : IDisposable
{
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
        out Direct2DParallelFrameMetrics metrics)
    {
        ThrowIfDisposed();
        frameLease = null;
        metrics = default;

        if (!Direct2DParallelRenderPlanner.TryCreatePlan(
                document,
                options,
                CadParallelRenderingMode.MultipleDevices,
                entities,
                width,
                height,
                out var plan))
        {
            if (!options.IsParallelRenderingEnabled ||
                options.ParallelRenderingMode != CadParallelRenderingMode.MultipleDevices)
                Reset();
            return false;
        }

        var activeDeviceCount = plan.WorkerCount;
        var batches = plan.Batches;
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
            metrics = new Direct2DParallelFrameMetrics(
                CadParallelRenderingMode.MultipleDevices,
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
            _deviceCount == deviceCount)
        {
            if (_width != width || _height != height)
            {
                foreach (var slot in _slots)
                    slot.Resize(mainD3DDevice, mainD2DContext, width, height);
                _width = width;
                _height = height;
            }
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
        IReadOnlyList<Direct2DParallelRenderBatch> batches)
    {
        var completed = new int[batches.Count];
        var workerStatistics = new CadRenderStatistics[batches.Count];
        try
        {
            Parallel.For(
                0,
                batches.Count,
                new ParallelOptions { MaxDegreeOfParallelism = batches.Count },
                index =>
                {
                    workerStatistics[index] = _slots![index].Draw(
                        document,
                        viewport,
                        options,
                        batches[index]);
                    Volatile.Write(ref completed[index], 1);
                });
            return workerStatistics;
        }
        catch
        {
            for (var index = 0; index < batches.Count; index++)
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
        IReadOnlyList<Direct2DParallelRenderBatch> batches)
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

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        try
        {
            if (_slots is not null && ReferenceEquals(_document, document))
                foreach (var slot in _slots)
                    slot.ApplyChanges(document, changes);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Reset();
        }
    }

    internal int PreparedEntityResourceCount => _slots?.Sum(slot => slot.PreparedEntityResourceCount) ?? 0;
    internal object? PoolIdentity => _slots;

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
        private Direct2DWorkerRenderTarget _target;

        private WorkerSlot(
            ID3D11Device d3dDevice,
            ID3D11DeviceContext d3dContext,
            IDXGIDevice dxgiDevice,
            ID2D1Factory1 d2dFactory,
            ID2D1Device d2dDevice,
            ID2D1DeviceContext d2dContext,
            Direct2DSceneRender renderer,
            Direct2DWorkerRenderTarget target)
        {
            _d3dDevice = d3dDevice;
            _d3dContext = d3dContext;
            _dxgiDevice = dxgiDevice;
            _d2dFactory = d2dFactory;
            _d2dDevice = d2dDevice;
            _d2dContext = d2dContext;
            _renderer = renderer;
            _target = target;
        }

        public ID2D1Bitmap1 MainReadableBitmap => _target.MainReadableBitmap;
        public int PreparedEntityResourceCount => _renderer.PreparedEntityResourceCount;

        public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
        {
            lock (_useGate) _renderer.ApplyParallelChanges(document, changes);
        }

        public void Resize(ID3D11Device mainDevice, ID2D1DeviceContext mainContext, int width, int height)
        {
            lock (_useGate)
            {
                _d2dContext.Target = null;
                var next = Direct2DWorkerRenderTarget.Create(_d3dDevice, _d2dContext,
                    mainDevice, mainContext, width, height, crossDevice: true);
                var previous = _target;
                _target = next;
                previous.Dispose();
            }
        }

        public static WorkerSlot Create(
            IDXGIAdapter adapter,
            ID3D11Device mainD3DDevice,
            ID2D1DeviceContext mainD2DContext,
            IDWriteFactory dwriteFactory,
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
            Direct2DWorkerRenderTarget? target = null;
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

                dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
                d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(
                    D2DFactoryType.SingleThreaded);
                d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
                d2dContext = d2dDevice.CreateDeviceContext(
                    DeviceContextOptions.EnableMultithreadedOptimizations);
                target = Direct2DWorkerRenderTarget.Create(d3dDevice, d2dContext,
                    mainD3DDevice, mainD2DContext, width, height, crossDevice: true);

                renderer = new Direct2DSceneRender();
                renderer.ResetDeviceResources(
                    d2dFactory,
                    dwriteFactory,
                    d2dDevice,
                    d2dContext,
                    document: null,
                    prepareBackgroundResources: false);

                return new WorkerSlot(
                    d3dDevice,
                    d3dContext,
                    dxgiDevice,
                    d2dFactory,
                    d2dDevice,
                    d2dContext,
                    renderer,
                    target);
            }
            catch
            {
                try { renderer?.Dispose(); } catch { }
                try { if (d2dContext is not null) d2dContext.Target = null; } catch { }
                try { target?.Dispose(); } catch { }
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
                    _target.AcquireForDraw();
                    mutexAcquired = true;
                    _d2dContext.Target = _target.Target;
                    _renderer.BeginFrame();
                    frameBegun = true;
                    _renderer.PrepareParallelEntityResources(document, entities);
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
                    try { _d2dContext.Target = null; } catch { }
                    if (mutexAcquired)
                        _target.FinishDraw(readyForMain);
                }
            }
        }

        public void AcquireForMainRead() =>
            _target.AcquireForRead();

        public void ReleaseToWorker() => _target.ReleaseRead();

        public void ReleaseUnreadFrame()
        {
            _target.ReleaseUnreadFrame();
        }

        public void Dispose()
        {
            lock (_useGate)
            {
                try { _renderer.Dispose(); } catch { }
                try { _d2dContext.Target = null; } catch { }
                _target.Dispose();
                _d2dContext.Dispose();
                _d2dDevice.Dispose();
                _d2dFactory.Dispose();
                _dxgiDevice.Dispose();
                try { _d3dContext.ClearState(); } catch { }
                _d3dContext.Dispose();
                _d3dDevice.Dispose();
            }
        }

    }
}

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
