using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Scene;

/// <summary>
/// Records scene command lists on one dedicated Direct2D device context. The worker shares
/// immutable device resources with the foreground renderer, but owns all mutable context state
/// and dynamic style resources.
/// </summary>
internal sealed class Direct2DChunkRecordingWorker : IDisposable
{
    private readonly Direct2DResourceCache _resourceCache;
    private readonly object _gate = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly ConcurrentQueue<RecordingResult> _completed = new();
    private Thread? _thread;
    private RecordingRequest? _pending;
    private bool _isRecording;
    private bool _stopping;
    private int _generation;
    private long _nextRequestId;
    private bool _disposed;

    public Direct2DChunkRecordingWorker(Direct2DResourceCache resourceCache)
    {
        _resourceCache = resourceCache;
    }

    public bool IsReady => _thread is { IsAlive: true };

    public void Reset(ID2D1Factory? factory, ID2D1Device? device)
    {
        ThrowIfDisposed();
        Stop();
        if (factory is null || device is null)
            return;

        ID2D1DeviceContext? context = null;
        Direct2DStyleResourceCache? styleResources = null;
        try
        {
            context = device.CreateDeviceContext(
                DeviceContextOptions.EnableMultithreadedOptimizations);
            styleResources = new Direct2DStyleResourceCache();
            styleResources.Reset(factory, context);
            var entityRenderer = new Direct2DEntityRenderer(
                _resourceCache,
                new Direct2DGeometryFactory(),
                styleResources,
                new Direct2DRenderStatisticsCollector());
            var workerContext = context;
            var workerStyleResources = styleResources;
            var thread = new Thread(
                () => Run(workerContext, workerStyleResources, entityRenderer))
            {
                IsBackground = true,
                Name = "Direct2D Chunk Recorder"
            };

            lock (_gate)
            {
                _stopping = false;
                try
                {
                    thread.Start();
                    _thread = thread;
                }
                catch
                {
                    _thread = null;
                    _stopping = true;
                    throw;
                }
            }

            context = null;
            styleResources = null;
        }
        finally
        {
            styleResources?.Dispose();
            context?.Dispose();
        }
    }

    public bool TrySchedule(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> entities,
        out long requestId)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_thread is not { IsAlive: true } ||
                _stopping ||
                _isRecording ||
                _pending is not null)
            {
                requestId = 0;
                return false;
            }

            requestId = ++_nextRequestId;
            _pending = new RecordingRequest(
                requestId,
                Volatile.Read(ref _generation),
                document,
                CloneViewport(viewport),
                options,
                entities);
        }

        _workAvailable.Set();
        return true;
    }

    public bool TryTakeCompleted(out RecordingResult result)
    {
        if (_completed.TryDequeue(out var completed) && completed is not null)
        {
            result = completed;
            return true;
        }

        result = null!;
        return false;
    }

    public void CancelAndWait()
    {
        ThrowIfDisposed();
        CancelAndWaitCore();
    }

    private void CancelAndWaitCore()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _pending = null;
            while (_isRecording)
                Monitor.Wait(_gate);
        }

        DisposeCompleted();
    }

    private void Run(
        ID2D1DeviceContext context,
        Direct2DStyleResourceCache styleResources,
        Direct2DEntityRenderer entityRenderer)
    {
        try
        {
            while (true)
            {
                _workAvailable.WaitOne();
                RecordingRequest? request;
                lock (_gate)
                {
                    if (_stopping)
                        return;

                    request = _pending;
                    _pending = null;
                    if (request is null)
                        continue;
                    _isRecording = true;
                }

                var result = Record(context, styleResources, entityRenderer, request);
                var publish =
                    !Volatile.Read(ref _stopping) &&
                    request.Generation == Volatile.Read(ref _generation);
                if (publish)
                    _completed.Enqueue(result);
                else
                    result.Dispose();

                lock (_gate)
                {
                    _isRecording = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _isRecording = false;
                Monitor.PulseAll(_gate);
            }
            styleResources.Dispose();
            context.Dispose();
        }
    }

    private RecordingResult Record(
        ID2D1DeviceContext context,
        Direct2DStyleResourceCache styleResources,
        Direct2DEntityRenderer entityRenderer,
        RecordingRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        ID2D1CommandList? commandList = null;
        var isDrawing = false;
        var completed = false;
        var recordedCount = 0;
        styleResources.BeginFrame();
        try
        {
            commandList = context.CreateCommandList();
            context.Target = commandList;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = request.Options.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = request.Options.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;

            foreach (var entity in request.Entities)
            {
                if (request.Generation != Volatile.Read(ref _generation))
                    return RecordingResult.Cancelled(request.RequestId);
                if (!IsVisibleForRecording(request.Document, entity) ||
                    !_resourceCache.TryGetEntityResources(entity.Id, out var resources) ||
                    resources is null)
                {
                    continue;
                }

                entityRenderer.Draw(
                    context,
                    request.Document,
                    entity,
                    resources,
                    request.Viewport,
                    request.Options);
                recordedCount++;
            }

            var endDrawResult = context.EndDraw();
            isDrawing = false;
            context.Target = null;
            if (endDrawResult.Failure)
                return RecordingResult.Failed(request.RequestId);

            commandList.Close();
            completed = true;
            return RecordingResult.Completed(
                request.RequestId,
                commandList,
                recordedCount,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch
        {
            return RecordingResult.Failed(request.RequestId);
        }
        finally
        {
            if (isDrawing)
                context.EndDraw();
            context.Target = null;
            styleResources.CompleteFrame();
            if (!completed)
                commandList?.Dispose();
        }
    }

    private static bool IsVisibleForRecording(CadDocument document, CadEntity entity)
    {
        return !entity.IsErased &&
               entity.IsVisible &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is { IsVisible: true, IsFrozen: false };
    }

    private static CadViewport CloneViewport(CadViewport source)
    {
        var clone = new CadViewport();
        clone.SetSize(source.ViewWidth, source.ViewHeight);
        clone.SetView(source.Zoom, source.Offset);
        return clone;
    }

    private void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null)
            {
                DisposeCompleted();
                return;
            }

            _stopping = true;
            _pending = null;
            Interlocked.Increment(ref _generation);
        }

        _workAvailable.Set();
        thread.Join();
        lock (_gate)
        {
            _thread = null;
            _isRecording = false;
        }
        DisposeCompleted();
    }

    private void DisposeCompleted()
    {
        while (_completed.TryDequeue(out var result))
            result.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _workAvailable.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DChunkRecordingWorker));
    }

    private sealed record RecordingRequest(
        long RequestId,
        int Generation,
        CadDocument Document,
        CadViewport Viewport,
        CadRenderOptions Options,
        IReadOnlyList<CadEntity> Entities);

    internal sealed class RecordingResult : IDisposable
    {
        public long RequestId { get; }
        public ID2D1CommandList? CommandList { get; private set; }
        public int RecordedEntityCount { get; }
        public double ElapsedMilliseconds { get; }
        public bool IsCancelled { get; }
        public bool IsFailed { get; }

        private RecordingResult(
            long requestId,
            ID2D1CommandList? commandList,
            int recordedEntityCount,
            double elapsedMilliseconds,
            bool isCancelled,
            bool isFailed)
        {
            RequestId = requestId;
            CommandList = commandList;
            RecordedEntityCount = recordedEntityCount;
            ElapsedMilliseconds = elapsedMilliseconds;
            IsCancelled = isCancelled;
            IsFailed = isFailed;
        }

        public static RecordingResult Completed(
            long requestId,
            ID2D1CommandList commandList,
            int recordedEntityCount,
            double elapsedMilliseconds) =>
            new(
                requestId,
                commandList,
                recordedEntityCount,
                elapsedMilliseconds,
                isCancelled: false,
                isFailed: false);

        public static RecordingResult Cancelled(long requestId) =>
            new(requestId, null, 0, 0, isCancelled: true, isFailed: false);

        public static RecordingResult Failed(long requestId) =>
            new(requestId, null, 0, 0, isCancelled: false, isFailed: true);

        public ID2D1CommandList? TakeCommandList()
        {
            var commandList = CommandList;
            CommandList = null;
            return commandList;
        }

        public void Dispose()
        {
            CommandList?.Dispose();
            CommandList = null;
        }
    }
}
