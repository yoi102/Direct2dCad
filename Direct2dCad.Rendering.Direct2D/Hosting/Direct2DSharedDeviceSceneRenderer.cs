using System.Diagnostics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using DxgiFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

/// <summary>
/// Draws ordered entity chunks with independent contexts and caches on one D2D device.
/// Worker targets are detached before the main context reads their textures.
/// </summary>
internal sealed class Direct2DSharedDeviceSceneRenderer : IDisposable
{
    private WorkerSlot[]? _slots;
    private CadDocument? _document;
    private nint _devicePointer;
    private int _width;
    private int _height;
    private int _workerCount;
    private bool _poolCreationFailed;
    private bool _disposed;

    public bool TryDraw(
        ID3D11Device d3dDevice,
        ID2D1Factory1 d2dFactory,
        ID2D1Device d2dDevice,
        ID2D1DeviceContext mainContext,
        IDWriteFactory dwriteFactory,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> entities,
        int width,
        int height,
        Action beforeComposite,
        out Direct2DParallelFrameMetrics metrics)
    {
        ThrowIfDisposed();
        metrics = default;
        if (!Direct2DParallelRenderPlanner.TryCreatePlan(
                options,
                CadParallelRenderingMode.SharedDeviceContexts,
                entities,
                width,
                height,
                out var plan))
        {
            if (!options.IsParallelRenderingEnabled ||
                options.ParallelRenderingMode != CadParallelRenderingMode.SharedDeviceContexts)
            {
                Reset();
            }
            return false;
        }

        var started = Stopwatch.GetTimestamp();
        CadRenderStatistics[] workerStatistics;
        try
        {
            if (!EnsurePool(
                    d3dDevice,
                    d2dFactory,
                    d2dDevice,
                    mainContext,
                    dwriteFactory,
                    document,
                    width,
                    height,
                    plan.WorkerCount))
            {
                return false;
            }

            workerStatistics = DrawOnWorkers(
                document,
                viewport,
                options,
                plan.Batches);
        }
        catch (Exception exception)
        {
            // Parallel rendering is optional. Rebuild the complete pool on any worker/device error.
            Debug.WriteLine(exception);
            Reset();
            return false;
        }

        try
        {
            // EndDraw has submitted every worker stream and each worker target is detached.
            beforeComposite();
            Composite(mainContext, plan.Batches.Count);
        }
        catch
        {
            Reset();
            throw;
        }

        metrics = new Direct2DParallelFrameMetrics(
            CadParallelRenderingMode.SharedDeviceContexts,
            plan.WorkerCount,
            entities.Count,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            workerStatistics);
        return true;
    }

    private bool EnsurePool(
        ID3D11Device d3dDevice,
        ID2D1Factory1 d2dFactory,
        ID2D1Device d2dDevice,
        ID2D1DeviceContext mainContext,
        IDWriteFactory dwriteFactory,
        CadDocument document,
        int width,
        int height,
        int workerCount)
    {
        if (_poolCreationFailed)
            return false;
        if (_slots is not null &&
            ReferenceEquals(_document, document) &&
            _devicePointer == d2dDevice.NativePointer &&
            _width == width &&
            _height == height &&
            _workerCount == workerCount)
        {
            return true;
        }

        Reset();
        var newSlots = new WorkerSlot[workerCount];
        try
        {
            for (var index = 0; index < workerCount; index++)
            {
                newSlots[index] = WorkerSlot.Create(
                    d3dDevice,
                    d2dFactory,
                    d2dDevice,
                    mainContext,
                    dwriteFactory,
                    document,
                    width,
                    height);
            }

            _slots = newSlots;
            _document = document;
            _devicePointer = d2dDevice.NativePointer;
            _width = width;
            _height = height;
            _workerCount = workerCount;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            foreach (var slot in newSlots)
            {
                try { slot?.Dispose(); } catch { }
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
        var statistics = new CadRenderStatistics[batches.Count];
        Parallel.For(
            0,
            batches.Count,
            new ParallelOptions { MaxDegreeOfParallelism = batches.Count },
            index =>
            {
                statistics[index] = _slots![index].Draw(
                    document,
                    viewport,
                    options,
                    batches[index]);
            });
        return statistics;
    }

    private void Composite(ID2D1DeviceContext mainContext, int batchCount)
    {
        for (var index = 0; index < batchCount; index++)
        {
            mainContext.DrawImage(
                _slots![index].MainReadableBitmap,
                null,
                null,
                Vortice.Direct2D1.InterpolationMode.NearestNeighbor,
                CompositeMode.SourceOver);
        }
    }

    public void Reset()
    {
        if (_slots is not null)
        {
            foreach (var slot in _slots)
            {
                try { slot.Dispose(); } catch { }
            }
        }

        _slots = null;
        _document = null;
        _devicePointer = 0;
        _width = 0;
        _height = 0;
        _workerCount = 0;
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
            throw new ObjectDisposedException(nameof(Direct2DSharedDeviceSceneRenderer));
    }

    private sealed class WorkerSlot : IDisposable
    {
        private readonly object _useGate = new();
        private readonly ID2D1DeviceContext _context;
        private readonly Direct2DSceneRender _renderer;
        private readonly ID3D11Texture2D _texture;
        private readonly ID2D1Bitmap1 _targetBitmap;

        private WorkerSlot(
            ID2D1DeviceContext context,
            Direct2DSceneRender renderer,
            ID3D11Texture2D texture,
            ID2D1Bitmap1 targetBitmap,
            ID2D1Bitmap1 mainReadableBitmap)
        {
            _context = context;
            _renderer = renderer;
            _texture = texture;
            _targetBitmap = targetBitmap;
            MainReadableBitmap = mainReadableBitmap;
        }

        public ID2D1Bitmap1 MainReadableBitmap { get; }

        public static WorkerSlot Create(
            ID3D11Device d3dDevice,
            ID2D1Factory1 d2dFactory,
            ID2D1Device d2dDevice,
            ID2D1DeviceContext mainContext,
            IDWriteFactory dwriteFactory,
            CadDocument document,
            int width,
            int height)
        {
            ID2D1DeviceContext? context = null;
            Direct2DSceneRender? renderer = null;
            ID3D11Texture2D? texture = null;
            ID2D1Bitmap1? targetBitmap = null;
            ID2D1Bitmap1? mainReadableBitmap = null;
            try
            {
                context = d2dDevice.CreateDeviceContext(
                    DeviceContextOptions.EnableMultithreadedOptimizations);
                texture = d3dDevice.CreateTexture2D(
                    CreateTextureDescription(width, height));
                targetBitmap = CreateBitmap(context, texture, BitmapOptions.Target);
                mainReadableBitmap = CreateBitmap(mainContext, texture, BitmapOptions.None);

                renderer = new Direct2DSceneRender();
                renderer.ResetDeviceResources(
                    d2dFactory,
                    dwriteFactory,
                    d2dDevice,
                    context,
                    document);

                return new WorkerSlot(
                    context,
                    renderer,
                    texture,
                    targetBitmap,
                    mainReadableBitmap);
            }
            catch
            {
                try { renderer?.Dispose(); } catch { }
                try { if (context is not null) context.Target = null; } catch { }
                try { mainReadableBitmap?.Dispose(); } catch { }
                try { targetBitmap?.Dispose(); } catch { }
                try { texture?.Dispose(); } catch { }
                try { context?.Dispose(); } catch { }
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
                var drawBegun = false;
                var frameBegun = false;
                try
                {
                    _context.Target = _targetBitmap;
                    _renderer.BeginFrame();
                    frameBegun = true;
                    _context.BeginDraw();
                    drawBegun = true;
                    _context.Transform = System.Numerics.Matrix3x2.Identity;
                    _context.Clear(new Color4(0, 0, 0, 0));
                    _renderer.RenderEntityBatch(document, viewport, options, entities);
                    _context.EndDraw().CheckError();
                    drawBegun = false;
                    _renderer.CompleteFrame();
                    frameBegun = false;
                    return _renderer.RenderStatistics;
                }
                finally
                {
                    if (drawBegun)
                    {
                        try { _context.EndDraw(); } catch { }
                    }
                    if (frameBegun)
                    {
                        try { _renderer.CompleteFrame(); } catch { }
                    }
                    // A D2D bitmap cannot remain bound as a target while another context reads
                    // the same DXGI surface during composition.
                    try { _context.Target = null; } catch { }
                }
            }
        }

        public void Dispose()
        {
            lock (_useGate)
            {
                try { _context.Target = null; } catch { }
                try { _renderer.Dispose(); } catch { }
                MainReadableBitmap.Dispose();
                _targetBitmap.Dispose();
                _texture.Dispose();
                _context.Dispose();
            }
        }

        private static Texture2DDescription CreateTextureDescription(int width, int height) =>
            new()
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
                MiscFlags = ResourceOptionFlags.None
            };

        private static ID2D1Bitmap1 CreateBitmap(
            ID2D1DeviceContext context,
            ID3D11Texture2D texture,
            BitmapOptions options)
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
                    BitmapOptions = options
                });
        }
    }
}
