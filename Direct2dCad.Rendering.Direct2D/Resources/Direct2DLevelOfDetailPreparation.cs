using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Vortice.Direct2D1;
using Bucket = Direct2dCad.Rendering.Direct2D.Resources.Direct2DResourceCache.EntityResourceBucket;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DLevelOfDetailPreparation : IDisposable
{
    private readonly Queue<EntityId> _queue = new();
    private readonly HashSet<EntityId> _queued = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task<Prepared[]>? _worker;
    private readonly Queue<Prepared> _ready = new();
    private bool _disposed;

    public void Request(EntityId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queued.Add(id))
            _queue.Enqueue(id);
    }

    public bool Prepare(CadDocument document, ID2D1Factory factory,
        IReadOnlyDictionary<EntityId, Bucket> resources, ResourcePreparationBudget budget, out bool changed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        changed = false;
        if (_worker is { IsCompleted: true })
        {
            var completed = _worker;
            _worker = null;
            foreach (var result in completed.GetAwaiter().GetResult())
                _ready.Enqueue(result);
        }

        while (_ready.Count > 0 && budget.TryStartItem())
        {
            using var result = _ready.Dequeue();
            if (!resources.TryGetValue(result.Work.Id, out var bucket) ||
                !ReferenceEquals(bucket, result.Work.Bucket) || bucket.LodRevision != result.Work.Revision)
                continue;
            bucket.MediumDetailGeometry = result.Medium;
            bucket.LowDetailGeometry = result.Low;
            result.Medium = result.Low = null;
            bucket.AreLevelOfDetailGeometriesInitialized = true;
            changed = true;
        }

        if (_worker is null && _ready.Count == 0)
        {
            var work = new List<Work>(16);
            while (_queue.Count > 0 && work.Count < 16 && budget.TryStartItem())
            {
                var id = _queue.Dequeue();
                _queued.Remove(id);
                if (!resources.TryGetValue(id, out var bucket) || bucket.AreLevelOfDetailGeometriesInitialized ||
                    !document.TryGetEntity(id, out var entity) || entity is null)
                    continue;
                var snapshot = Capture(entity, bucket);
                if (snapshot is null)
                    bucket.AreLevelOfDetailGeometriesInitialized = true;
                else
                    work.Add(snapshot);
            }
            if (work.Count > 0)
            {
                var ownedFactory = factory.QueryInterface<ID2D1Factory>();
                var token = _cancellation.Token;
                _worker = Task.Run(() => Build(work, ownedFactory, token));
            }
        }
        return _worker is not null || _ready.Count > 0 || _queue.Count > 0;
    }

    private static Work? Capture(CadEntity entity, Bucket bucket)
    {
        var extent = Math.Max(entity.Bounds.Width, entity.Bounds.Height);
        if (!double.IsFinite(extent) || extent <= double.Epsilon)
            return null;
        return entity switch
        {
            CadPolyline line when line.Points.Count > 16 =>
                new(entity.Id, bucket, bucket.LodRevision, line.Points.ToArray(), false, line.Closed, extent),
            CadSpline { Closed: true, FillStyleId: not null } => null,
            CadSpline spline when spline.FitPoints.Count > 16 =>
                new(entity.Id, bucket, bucket.LodRevision, spline.FitPoints.ToArray(), true, spline.Closed, extent),
            _ => null
        };
    }

    private static Prepared[] Build(List<Work> work, ID2D1Factory factory, CancellationToken token)
    {
        using (factory)
        {
            var geometryFactory = new Direct2DGeometryFactory();
            var results = new List<Prepared>(work.Count);
            try
            {
                foreach (var item in work)
                {
                    token.ThrowIfCancellationRequested();
                    var result = new Prepared(item);
                    results.Add(result);
                    var points = item.IsSpline ? Flatten(item) : item.Points;
                    var medium = CadPointLodSimplifier.Simplify(points, item.Closed, item.Extent / 1024);
                    if (IsWorthCaching(medium.Count, points.Length, item.Closed))
                        result.Medium = geometryFactory.CreatePolyline(factory, medium, item.Closed);
                    token.ThrowIfCancellationRequested();
                    var low = CadPointLodSimplifier.Simplify(points, item.Closed, item.Extent / 256);
                    if (IsWorthCaching(low.Count, Math.Min(medium.Count, points.Length), item.Closed))
                        result.Low = geometryFactory.CreatePolyline(factory, low, item.Closed);
                }
                token.ThrowIfCancellationRequested();
                return results.ToArray();
            }
            catch
            {
                foreach (var result in results)
                    result.Dispose();
                throw;
            }
        }
    }

    private static CadPointD[] Flatten(Work work)
    {
        var segments = CadSpline.CreateBezierSegments(work.Points, work.Closed);
        var points = new CadPointD[segments.Count * 6 + 1];
        points[0] = segments[0].Start;
        var index = 1;
        foreach (var segment in segments)
            for (var step = 1; step <= 6; step++)
                points[index++] = segment.Evaluate(step / 6.0);
        return points;
    }

    private static bool IsWorthCaching(int count, int sourceCount, bool closed) =>
        count >= (closed ? 3 : 2) && count <= sourceCount * 3 / 4;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _queue.Clear();
        _queued.Clear();
        while (_ready.TryDequeue(out var result))
            result.Dispose();
        if (_worker is { } worker)
        {
            _worker = null;
            _ = worker.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                    foreach (var result in task.Result)
                        result.Dispose();
                else
                    _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    // The worker reads only copied values, never the bucket or live entity.
    private sealed record Work(EntityId Id, Bucket Bucket, int Revision, CadPointD[] Points,
        bool IsSpline, bool Closed, double Extent);

    private sealed class Prepared(Work work) : IDisposable
    {
        public Work Work { get; } = work;
        public ID2D1Geometry? Medium;
        public ID2D1Geometry? Low;
        public void Dispose() { Medium?.Dispose(); Low?.Dispose(); }
    }
}
