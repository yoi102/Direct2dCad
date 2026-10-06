namespace Direct2dCad.Rendering;

/// <summary>
/// Shares the estimated retained-cache allowance across open documents and their render workers.
/// This is not a driver VRAM limit: active frame resources and presentation surfaces are excluded.
/// Backends trim on their owner thread at frame/cache-preparation boundaries.
/// </summary>
public sealed class CadRenderResourceBudget
{
    public static CadRenderResourceBudget Shared { get; } = new();
    private readonly object _gate = new();
    private readonly HashSet<DocumentLease> _documents = [];
    public long ProcessLimitBytes { get; }
    public long DocumentLimitBytes { get; }

    public CadRenderResourceBudget(long processLimitBytes = 1024L * 1024 * 1024,
        long documentLimitBytes = 512L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processLimitBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentLimitBytes);
        ProcessLimitBytes = processLimitBytes;
        DocumentLimitBytes = documentLimitBytes;
    }

    public DocumentLease RegisterDocument()
    {
        DocumentLease result;
        DocumentLease[] documents;
        lock (_gate)
        {
            result = new DocumentLease(this);
            _documents.Add(result);
            documents = [.. _documents];
        }
        Notify(documents);
        return result;
    }

    public CadRenderResourceStatistics Statistics
    {
        get
        {
            lock (_gate)
                return new(ProcessLimitBytes, _documents.Sum(d => d.EstimatedBytes),
                    _documents.Count, _documents.Sum(d => d.Renderers.Count));
        }
    }

    private static void Notify(IEnumerable<DocumentLease> documents)
    {
        foreach (var document in documents)
            document.NotifyBudgetChanged();
    }

    public sealed class DocumentLease : IDisposable
    {
        private readonly CadRenderResourceBudget _owner;
        internal readonly HashSet<RendererLease> Renderers = [];
        private bool _disposed;
        internal long EstimatedBytes => Renderers.Sum(r => r.EstimatedBytes);
        public event EventHandler? BudgetChanged;
        internal void NotifyBudgetChanged()
        {
            // Scheduling is advisory; a closing presentation surface must not prevent
            // another document from acquiring or releasing its resource allowance.
            foreach (var handler in BudgetChanged?.GetInvocationList() ?? [])
                try { ((EventHandler)handler)(this, EventArgs.Empty); }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }
        internal DocumentLease(CadRenderResourceBudget owner) => _owner = owner;
        public long LimitBytes
        {
            get { lock (_owner._gate) return _disposed ? 0 : Math.Min(_owner.DocumentLimitBytes,
                _owner.ProcessLimitBytes / Math.Max(1, _owner._documents.Count)); }
        }
        public CadRenderResourceStatistics Statistics
        {
            get { lock (_owner._gate) return new(LimitBytes, EstimatedBytes, _disposed ? 0 : 1, Renderers.Count); }
        }
        public RendererLease RegisterRenderer()
        {
            RendererLease result;
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                result = new RendererLease(this);
                Renderers.Add(result);
            }
            NotifyBudgetChanged();
            return result;
        }
        public void Dispose()
        {
            DocumentLease[] remaining;
            lock (_owner._gate)
            {
                if (_disposed) return;
                _disposed = true;
                _owner._documents.Remove(this);
                Renderers.Clear();
                BudgetChanged = null;
                remaining = [.. _owner._documents];
            }
            Notify(remaining);
        }

        public sealed class RendererLease : IDisposable
        {
            private readonly DocumentLease _document;
            private bool _disposed;
            internal long EstimatedBytes;
            internal RendererLease(DocumentLease document) => _document = document;
            public long LimitBytes
            {
                get { lock (_document._owner._gate) return _disposed ? 0 :
                    _document.LimitBytes / Math.Max(1, _document.Renderers.Count); }
            }
            public void Report(long estimatedRetainedBytes)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(estimatedRetainedBytes);
                lock (_document._owner._gate)
                {
                    if (!_disposed) EstimatedBytes = estimatedRetainedBytes;
                }
            }
            public void Dispose()
            {
                lock (_document._owner._gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    EstimatedBytes = 0;
                    _document.Renderers.Remove(this);
                }
                _document.NotifyBudgetChanged();
            }
        }
    }
}

public readonly record struct CadRenderResourceStatistics(
    long RetainedCacheLimitBytes,
    long EstimatedRetainedCacheBytes,
    int DocumentCount,
    int RendererCount);
