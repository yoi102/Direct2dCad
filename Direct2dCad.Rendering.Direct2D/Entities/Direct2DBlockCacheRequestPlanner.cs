using System.Diagnostics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal sealed class Direct2DBlockCacheRequestPlanner
{
    private const int MaximumPreparedDefinitionKeys = 128;
    private const double BuildBudgetMilliseconds = 1.5;
    private readonly CadDocument _document;
    private readonly CadViewport _viewport;
    private readonly CadRenderOptions _options;
    private readonly IReadOnlyList<CadEntity> _orderedEntities;
    private readonly Direct2DBlockCacheRequestProfileKey _profileKey;
    private readonly Func<BlockId, bool> _isDefinitionCacheable;
    private readonly Func<BlockId, IReadOnlySet<EntityId>> _resolveDefinitionDependencies;
    private readonly Dictionary<Direct2DBlockDefinitionCacheKey, RequestCandidate> _candidates = [];
    private readonly List<RequestCandidate> _candidateList = [];
    private readonly PriorityQueue<RequestCandidate, RequestCandidatePriority> _selected =
        new(MaximumPreparedDefinitionKeys, RequestCandidateWorstFirstComparer.Instance);
    private readonly List<Direct2DBlockDefinitionCacheRequest> _requests =
        new(MaximumPreparedDefinitionKeys);
    private BuildPhase _phase;
    private int _scanIndex;
    private int _selectionIndex;

    public Direct2DBlockCacheRequestPlanner(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> orderedEntities,
        Direct2DBlockCacheRequestProfileKey profileKey,
        Func<BlockId, bool> isDefinitionCacheable,
        Func<BlockId, IReadOnlySet<EntityId>> resolveDefinitionDependencies)
    {
        _document = document;
        _viewport = viewport;
        _options = options;
        _orderedEntities = orderedEntities;
        _profileKey = profileKey;
        _isDefinitionCacheable = isDefinitionCacheable;
        _resolveDefinitionDependencies = resolveDefinitionDependencies;
    }

    public IReadOnlyList<Direct2DBlockDefinitionCacheRequest> Requests => _requests;

    public bool Matches(
        CadDocument document,
        Direct2DBlockCacheRequestProfileKey profileKey,
        IReadOnlyList<CadEntity> orderedEntities) =>
        ReferenceEquals(_document, document) &&
        ReferenceEquals(_orderedEntities, orderedEntities) &&
        _profileKey.Equals(profileKey);

    public bool BuildStep()
    {
        if (_phase == BuildPhase.Complete)
            return true;

        var started = Stopwatch.GetTimestamp();
        do
        {
            switch (_phase)
            {
                case BuildPhase.Scan:
                    if (_scanIndex < _orderedEntities.Count)
                    {
                        ScanEntity(_orderedEntities[_scanIndex++]);
                        break;
                    }

                    _phase = BuildPhase.Select;
                    break;

                case BuildPhase.Select:
                    if (_selectionIndex < _candidateList.Count)
                    {
                        SelectCandidate(_candidateList[_selectionIndex++]);
                        break;
                    }

                    CompleteSelection();
                    _phase = BuildPhase.Complete;
                    return true;
            }
        }
        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < BuildBudgetMilliseconds);

        return _phase == BuildPhase.Complete;
    }

    private void ScanEntity(CadEntity entity)
    {
        if (entity is not CadBlockReference reference ||
            Direct2DEntityLevelOfDetail.Resolve(
                reference,
                resources: null,
                _viewport,
                _options) != Direct2DEntityRenderDetail.Full ||
            !Direct2DBlockReferenceStyleResolver.TryResolve(
                _document,
                Direct2DBlockReferenceRenderState.From(reference),
                parentStyle: null,
                out var style) ||
            !Direct2DBlockReferenceStyleResolver.IsVisible(
                _document,
                reference,
                style,
                _options) ||
            !_isDefinitionCacheable(reference.DefinitionBlockId))
        {
            return;
        }

        var key = Direct2DBlockCacheKeyFactory.Create(
            reference.DefinitionBlockId,
            style,
            _viewport,
            _options,
            Math.Abs(reference.ScaleX) *
            _viewport.Zoom *
            Direct2DBlockCacheKeyFactory.ResolveScaleMultiplier(_options),
            Math.Abs(reference.ScaleY) *
            _viewport.Zoom *
            Direct2DBlockCacheKeyFactory.ResolveScaleMultiplier(_options));
        if (_candidates.TryGetValue(key, out var existing))
        {
            existing.ReferenceCount++;
            return;
        }

        var dependencies = _resolveDefinitionDependencies(reference.DefinitionBlockId);
        var request = new Direct2DBlockDefinitionCacheRequest(
            key,
            style,
            Direct2DRenderScaleBucket.Quantize(_viewport.Zoom),
            Math.Max(
                BitConverter.Int64BitsToDouble(key.ScreenScaleXBits),
                BitConverter.Int64BitsToDouble(key.ScreenScaleYBits)),
            dependencies);
        var candidate = new RequestCandidate(request);
        _candidates.Add(key, candidate);
        _candidateList.Add(candidate);
    }

    private void SelectCandidate(RequestCandidate candidate)
    {
        if (candidate.ReferenceCount < 2)
            return;

        var priority = RequestCandidatePriority.From(candidate);
        if (_selected.Count < MaximumPreparedDefinitionKeys)
        {
            _selected.Enqueue(candidate, priority);
            return;
        }

        _selected.TryPeek(out var worst, out _);
        if (worst is null || !IsBetter(candidate, worst))
            return;

        _selected.Dequeue();
        _selected.Enqueue(candidate, priority);
    }

    private void CompleteSelection()
    {
        var selected = new List<RequestCandidate>(_selected.Count);
        while (_selected.TryDequeue(out var candidate, out _))
            selected.Add(candidate);

        selected.Sort(RequestCandidateBestFirstComparer.Instance);
        foreach (var candidate in selected)
            _requests.Add(candidate.Request);
    }

    private static bool IsBetter(RequestCandidate candidate, RequestCandidate other)
    {
        var countComparison = candidate.ReferenceCount.CompareTo(other.ReferenceCount);
        return countComparison > 0 ||
               countComparison == 0 &&
               candidate.Request.Key.DefinitionBlockId.Value <
               other.Request.Key.DefinitionBlockId.Value;
    }

    private enum BuildPhase
    {
        Scan,
        Select,
        Complete
    }

    private readonly record struct RequestCandidatePriority(
        int ReferenceCount,
        long DefinitionBlockId)
    {
        public static RequestCandidatePriority From(RequestCandidate candidate) => new(
            candidate.ReferenceCount,
            candidate.Request.Key.DefinitionBlockId.Value);
    }

    private sealed class RequestCandidateWorstFirstComparer :
        IComparer<RequestCandidatePriority>
    {
        public static RequestCandidateWorstFirstComparer Instance { get; } = new();

        public int Compare(RequestCandidatePriority x, RequestCandidatePriority y)
        {
            var countComparison = x.ReferenceCount.CompareTo(y.ReferenceCount);
            return countComparison != 0
                ? countComparison
                : y.DefinitionBlockId.CompareTo(x.DefinitionBlockId);
        }
    }

    private sealed class RequestCandidateBestFirstComparer : IComparer<RequestCandidate>
    {
        public static RequestCandidateBestFirstComparer Instance { get; } = new();

        public int Compare(RequestCandidate? x, RequestCandidate? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return 1;
            if (y is null)
                return -1;

            var countComparison = y.ReferenceCount.CompareTo(x.ReferenceCount);
            return countComparison != 0
                ? countComparison
                : x.Request.Key.DefinitionBlockId.Value.CompareTo(
                    y.Request.Key.DefinitionBlockId.Value);
        }
    }

    private sealed class RequestCandidate(Direct2DBlockDefinitionCacheRequest request)
    {
        public Direct2DBlockDefinitionCacheRequest Request { get; } = request;

        public int ReferenceCount { get; set; } = 1;
    }
}
