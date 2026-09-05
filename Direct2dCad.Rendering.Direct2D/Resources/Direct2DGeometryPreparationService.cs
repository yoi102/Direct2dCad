using System.Threading.Channels;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DGeometryPreparationService(ID2D1Factory factory) : IDisposable
{
    private readonly ID2D1Factory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private const int ReadyGeometryCapacity = 256;
    private readonly HashSet<EntityId> _invalidated = [];
    private readonly HashSet<EntityId> _pendingIds = [];
    private readonly Queue<EntityId> _priority = [];
    private CadDocument? _document;
    private EntityId[] _entityIds = [];
    private int _captureIndex;
    private Channel<GeometryPreparationSnapshot>? _snapshots;
    private GeometryPreparationSnapshot? _waitingSnapshot;
    private Channel<Direct2DPreparedGeometry>? _ready;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _disposed;
    private bool _priorityChosen;
    private bool _captureStarted;

    public bool NeedsPriority => !_priorityChosen && _document is not null && !_captureStarted;

    public void Schedule(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ThrowIfDisposed();
        Cancel();
        _document = document;
        _entityIds = document.Entities.Keys.ToArray();
        _captureIndex = 0;
        _priorityChosen = false;
        _captureStarted = false;
        _pendingIds.UnionWith(_entityIds);
        _priority.Clear();
        _invalidated.Clear();
        var snapshots = Channel.CreateBounded<GeometryPreparationSnapshot>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _snapshots = snapshots;
        var channel = Channel.CreateBounded<Direct2DPreparedGeometry>(new BoundedChannelOptions(ReadyGeometryCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });
        _ready = channel;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        // The worker owns a COM reference until it has stopped, including after cancellation.
        var workerFactory = _factory.QueryInterface<ID2D1Factory>();
        _worker = Task.Run(async () =>
        {
            using (workerFactory)
            {
                try
                {
                    await foreach (var snapshot in snapshots.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        var prepared = snapshot.Prepare(workerFactory);
                        try { await channel.Writer.WriteAsync(prepared, token).ConfigureAwait(false); }
                        catch { prepared.Dispose(); throw; }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (ChannelClosedException) when (token.IsCancellationRequested) { }
                finally { channel.Writer.TryComplete(); }
            }
        });
    }

    // Called only by the document/render owner; the worker never reads live entities.
    public void CaptureStep(ResourcePreparationBudget budget)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(budget);
        if (_snapshots is null || _document is null)
            return;
        _captureStarted = true;
        while (budget.TryStartItem())
        {
            if (_waitingSnapshot is { } waiting)
            {
                if (!_snapshots.Writer.TryWrite(waiting))
                    return;
                _waitingSnapshot = null;
                continue;
            }
            if (_pendingIds.Count == 0)
            {
                _snapshots.Writer.TryComplete();
                _document = null;
                _entityIds = [];
                _priority.Clear();
                return;
            }
            EntityId id;
            if (!_priority.TryDequeue(out id))
            {
                if (_captureIndex == _entityIds.Length)
                {
                    _snapshots.Writer.TryComplete();
                    _document = null;
                    return;
                }
                id = _entityIds[_captureIndex++];
            }
            if (!_pendingIds.Remove(id) || _invalidated.Contains(id) ||
                !_document.TryGetEntity(id, out var entity) || entity is null)
                continue;
            _waitingSnapshot = GeometryPreparationSnapshot.Capture(_document, entity);
        }
    }

    public void Prioritize(IEnumerable<EntityId> ids)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        if (!NeedsPriority || _document is null)
            return;
        _priorityChosen = true;
        var visited = new HashSet<EntityId>();
        var pending = new Queue<EntityId>();
        foreach (var id in ids)
            if (visited.Add(id))
                pending.Enqueue(id);
        // Include nested definitions without changing document draw order.
        while (pending.TryDequeue(out var id))
        {
            _priority.Enqueue(id);
            if (!_document.TryGetEntity(id, out var entity) ||
                entity is not CadBlockReference reference ||
                !_document.TryGetBlock(reference.DefinitionBlockId, out var block) || block is null)
                continue;
            foreach (var childId in block.EntityIds)
                if (visited.Add(childId))
                {
                    pending.Enqueue(childId);
                }
        }
    }

    public void Invalidate(IEnumerable<EntityId> ids)
    {
        ThrowIfDisposed();
        foreach (var id in ids)
            _invalidated.Add(id);
    }

    public void Invalidate(EntityId id)
    {
        ThrowIfDisposed();
        _invalidated.Add(id);
    }

    public bool TryTakeNext(out Direct2DPreparedGeometry? prepared) =>
        TryTakeNext(out prepared, new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));

    internal bool TryTakeNext(out Direct2DPreparedGeometry? prepared, ResourcePreparationBudget budget)
    {
        ThrowIfDisposed();
        while (_ready is not null && budget.TryStartItem() && _ready.Reader.TryRead(out prepared))
        {
            if (!_invalidated.Contains(prepared.EntityId))
                return true;
            prepared.Dispose();
        }
        prepared = null;
        if (_worker is { IsCompleted: true })
            _worker.GetAwaiter().GetResult();
        return false;
    }

    public bool IsPending
    {
        get
        {
            ThrowIfDisposed();
            if (_worker is { IsCompleted: true })
                _worker.GetAwaiter().GetResult();
            return _document is not null || _worker is { IsCompleted: false } || _ready?.Reader.TryPeek(out _) == true;
        }
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        _snapshots?.Writer.TryComplete();
        _snapshots = null;
        _waitingSnapshot = null;
        _document = null;
        _entityIds = [];
        _priority.Clear();
        _pendingIds.Clear();
        _ready?.Writer.TryComplete();
        if (_ready is not null)
            while (_ready.Reader.TryRead(out var item))
                item.Dispose();
        if (_worker is not null)
            _ = _worker.ContinueWith(static task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        _cancellation?.Dispose();
        _cancellation = null;
        _ready = null;
        _worker = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Cancel();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
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
