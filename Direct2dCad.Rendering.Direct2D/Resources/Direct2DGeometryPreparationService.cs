using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DGeometryPreparationService : IDisposable
{
    private readonly object _gate = new();
    private readonly ID2D1Factory _factory;
    private Task<IReadOnlyList<Direct2DPreparedGeometry>>? _pendingTask;
    private CancellationTokenSource? _pendingCancellation;
    private IReadOnlyList<Direct2DPreparedGeometry>? _completed;
    private int _nextIndex;
    private bool _disposed;

    public Direct2DGeometryPreparationService(ID2D1Factory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Schedule(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();

        var snapshot = document.Entities.Values
            .Select(entity => new GeometryPreparationSnapshot(
                entity,
                RequiresPrimitiveFillGeometry(document, entity)))
            .ToArray();

        lock (_gate)
        {
            CancelPendingLocked();
            DisposePreparedGeometryList(_completed);
            _completed = null;
            _nextIndex = 0;
            _pendingCancellation = new CancellationTokenSource();
            var cancellationToken = _pendingCancellation.Token;
            _pendingTask = Task.Run(
                () => Build(snapshot, cancellationToken),
                cancellationToken);
        }
    }

    public bool TryTakeNext(out Direct2DPreparedGeometry? prepared)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            EnsureCompletedLocked();
            if (_completed is null || _nextIndex >= _completed.Count)
            {
                prepared = null;
                return false;
            }

            prepared = _completed[_nextIndex++];
            return true;
        }
    }

    public bool IsPending
    {
        get
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                if (_pendingTask is { IsCompleted: false })
                    return true;

                EnsureCompletedLocked();
                return _completed is not null && _nextIndex < _completed.Count;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            CancelPendingLocked();
            DisposePreparedGeometryList(_completed);
            _completed = null;
        }

        _disposed = true;
    }

    private IReadOnlyList<Direct2DPreparedGeometry> Build(
        IReadOnlyList<GeometryPreparationSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        using var workerCache = new Direct2DResourceCache(
            new Direct2DStyleResourceCache(),
            new Direct2DTextFormatResourceCache(),
            new Direct2DRenderStatisticsCollector(),
            _factory);
        var result = new List<Direct2DPreparedGeometry>(snapshot.Count);

        try
        {
            foreach (var item in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(workerCache.CreatePreparedGeometry(
                    item.Entity,
                    item.RequiresPrimitiveFillGeometry));
            }

            return result;
        }
        catch
        {
            DisposePreparedGeometryList(result);
            throw;
        }
    }

    private void EnsureCompletedLocked()
    {
        if (_completed is not null || _pendingTask is null || !_pendingTask.IsCompleted)
            return;

        var task = _pendingTask;
        _pendingTask = null;
        _pendingCancellation?.Dispose();
        _pendingCancellation = null;

        if (task.IsCanceled)
            return;

        _completed = task.GetAwaiter().GetResult();
        _nextIndex = 0;
    }

    private void CancelPendingLocked()
    {
        var task = _pendingTask;
        _pendingCancellation?.Cancel();
        _pendingCancellation?.Dispose();
        _pendingCancellation = null;
        _pendingTask = null;

        if (task is null)
            return;

        if (task.IsCompletedSuccessfully)
        {
            DisposePreparedGeometryList(task.GetAwaiter().GetResult());
            return;
        }

        _ = task.ContinueWith(
            static completedTask =>
            {
                if (completedTask.IsCompletedSuccessfully)
                    DisposePreparedGeometryList(completedTask.GetAwaiter().GetResult());
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool RequiresPrimitiveFillGeometry(
        CadDocument document,
        CadEntity entity)
    {
        var fillStyleId = entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            CadSpline { Closed: true } spline => spline.FillStyleId,
            CadCompositePath { Closed: true } path => path.FillStyleId,
            _ => null
        };

        return fillStyleId is not null &&
               document.TryGetStyle(fillStyleId.Value, out var style) &&
               style is CadHatchFillStyle;
    }

    private static void DisposePreparedGeometryList(
        IReadOnlyList<Direct2DPreparedGeometry>? prepared)
    {
        if (prepared is null)
            return;

        foreach (var item in prepared)
            item.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DGeometryPreparationService));
    }

    private readonly record struct GeometryPreparationSnapshot(
        CadEntity Entity,
        bool RequiresPrimitiveFillGeometry);
}

internal sealed class Direct2DPreparedGeometry(
    EntityId entityId,
    ID2D1Geometry? geometry,
    int complexity) : IDisposable
{
    public EntityId EntityId { get; } = entityId;
    private ID2D1Geometry? _geometry = geometry;
    public ID2D1Geometry? Geometry => _geometry;
    public int Complexity { get; } = complexity;

    public ID2D1Geometry? TakeGeometry() => Interlocked.Exchange(ref _geometry, null);

    public void Dispose() => Interlocked.Exchange(ref _geometry, null)?.Dispose();
}
