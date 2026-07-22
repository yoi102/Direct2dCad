using System.Diagnostics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal delegate ID2D1CommandList? Direct2DBlockDefinitionRecordCallback(
    Direct2DBlockDefinitionCacheRequest request,
    out int recordedEntityCount);

internal sealed class Direct2DBlockDefinitionCommandListCache(
    Direct2DRenderStatisticsCollector statistics) : IDisposable
{
    private const int MaximumEntries = 128;
    private const double BuildBudgetMilliseconds = 4.0;
    internal const long CacheBudgetBytes = 32L * 1024 * 1024;

    private readonly Dictionary<Direct2DBlockDefinitionCacheKey, Entry> _entries = [];
    private readonly HashSet<Direct2DBlockDefinitionCacheKey> _failedKeys = [];
    private readonly HashSet<Direct2DBlockDefinitionCacheKey> _budgetEvictedKeys = [];
    private readonly List<Direct2DBlockDefinitionCacheKey> _staleKeys = [];
    private long _usageStamp;
    private long _estimatedBytes;
    private bool _disposed;

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

    public bool Prepare(
        IReadOnlyList<Direct2DBlockDefinitionCacheRequest> requests,
        bool buildStep,
        Direct2DBlockDefinitionRecordCallback record)
    {
        ThrowIfDisposed();
        if (requests.Count == 0)
            return false;

        if (!buildStep)
            return HasPendingBuild(requests);

        var started = Stopwatch.GetTimestamp();
        foreach (var request in requests)
        {
            if (_entries.ContainsKey(request.Key) ||
                _failedKeys.Contains(request.Key) ||
                _budgetEvictedKeys.Contains(request.Key))
                continue;

            var commandList = record(request, out var recordedEntityCount);
            if (commandList is null)
            {
                _failedKeys.Add(request.Key);
            }
            else
            {
                var entry = new Entry(
                    commandList,
                    request.DependencyEntityIds,
                    recordedEntityCount,
                    EstimateCommandListBytes(recordedEntityCount, request.DependencyEntityIds.Count))
                {
                    LastUsed = ++_usageStamp
                };
                _entries.Add(request.Key, entry);
                _estimatedBytes += entry.EstimatedBytes;
                statistics.RecordBlockDefinitionCommandListBuild();
                TrimEntries(request.Key);
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= BuildBudgetMilliseconds)
                break;
        }

        return HasPendingBuild(requests);
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        Direct2DBlockDefinitionCacheKey key)
    {
        ThrowIfDisposed();
        if (!_entries.TryGetValue(key, out var entry))
            return false;

        context.DrawImage(
            entry.CommandList,
            null,
            null,
            InterpolationMode.Linear,
            CompositeMode.SourceOver);
        entry.LastUsed = ++_usageStamp;
        statistics.RecordBlockDefinitionCommandListReplay();
        statistics.RecordExpandedBlockEntities(entry.RecordedEntityCount);
        return true;
    }

    public void ApplyChanges(CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        if (!changes.DocumentChanged)
            return;
        if (changes.AffectsDocumentStructure ||
            changes.AffectsViewSettings ||
            changes.AffectsLayouts ||
            changes.AffectsLayoutStructure)
        {
            Clear();
            return;
        }

        _staleKeys.Clear();
        foreach (var pair in _entries)
        {
            if (DependsOnAny(pair.Value, changes.EntityChanges))
                _staleKeys.Add(pair.Key);
        }

        foreach (var key in _staleKeys)
            RemoveEntry(key);
        if (_staleKeys.Count > 0)
        {
            _failedKeys.Clear();
            _budgetEvictedKeys.Clear();
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        foreach (var entry in _entries.Values)
            entry.Dispose();
        _entries.Clear();
        _failedKeys.Clear();
        _budgetEvictedKeys.Clear();
        _staleKeys.Clear();
        _estimatedBytes = 0;
    }

    private bool HasPendingBuild(IReadOnlyList<Direct2DBlockDefinitionCacheRequest> requests)
    {
        foreach (var request in requests)
        {
            if (!_entries.ContainsKey(request.Key) &&
                !_failedKeys.Contains(request.Key) &&
                !_budgetEvictedKeys.Contains(request.Key))
                return true;
        }

        return false;
    }

    private static bool DependsOnAny(
        Entry entry,
        IReadOnlyList<CadEntityChange> changes)
    {
        foreach (var change in changes)
        {
            if (entry.DependencyEntityIds.Contains(change.EntityId))
                return true;
        }

        return false;
    }

    private void TrimEntries(Direct2DBlockDefinitionCacheKey protectedKey)
    {
        while (_entries.Count > MaximumEntries || EstimatedBytes > CacheBudgetBytes)
        {
            var hasCandidate = false;
            var oldestKey = default(Direct2DBlockDefinitionCacheKey);
            var oldestUsage = long.MaxValue;
            foreach (var pair in _entries)
            {
                if (pair.Key.Equals(protectedKey) || pair.Value.LastUsed >= oldestUsage)
                    continue;
                hasCandidate = true;
                oldestKey = pair.Key;
                oldestUsage = pair.Value.LastUsed;
            }

            if (!hasCandidate)
                return;
            RemoveEntry(oldestKey, budgetEviction: true);
            statistics.RecordGpuCacheEviction();
        }
    }

    private void RemoveEntry(
        Direct2DBlockDefinitionCacheKey key,
        bool budgetEviction = false)
    {
        if (_entries.Remove(key, out var entry))
        {
            _estimatedBytes -= entry.EstimatedBytes;
            entry.Dispose();
            if (budgetEviction)
                _budgetEvictedKeys.Add(key);
        }
    }

    private static long EstimateCommandListBytes(int entityCount, int dependencyCount) =>
        4L * 1024 + entityCount * 256L + dependencyCount * 32L;

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DBlockDefinitionCommandListCache));
    }

    private sealed class Entry(
        ID2D1CommandList commandList,
        IReadOnlySet<EntityId> dependencyEntityIds,
        int recordedEntityCount,
        long estimatedBytes) : IDisposable
    {
        public ID2D1CommandList CommandList { get; } = commandList;
        public IReadOnlySet<EntityId> DependencyEntityIds { get; } = dependencyEntityIds;
        public int RecordedEntityCount { get; } = recordedEntityCount;
        public long EstimatedBytes { get; } = estimatedBytes;
        public long LastUsed { get; set; }

        public void Dispose() => CommandList.Dispose();
    }
}

internal readonly record struct Direct2DBlockDefinitionCacheKey(
    BlockId DefinitionBlockId,
    LayerId EffectiveLayerId,
    CadColor ReferenceColor,
    long EffectiveLayerStrokeWidthBits,
    long ViewZoomBits,
    long ScreenScaleXBits,
    long ScreenScaleYBits,
    bool IsAntialiasingEnabled,
    bool IsTextAntialiasingEnabled,
    bool IsLevelOfDetailEnabled,
    bool KeepStrokeWidthScreenConstant,
    long MinimumScreenStrokeWidthBits);

internal readonly record struct Direct2DBlockDefinitionCacheRequest(
    Direct2DBlockDefinitionCacheKey Key,
    Direct2DBlockRenderStyle Style,
    double BuildViewZoom,
    double BuildScreenScale,
    IReadOnlySet<EntityId> DependencyEntityIds);

internal readonly record struct Direct2DBlockRenderStyle(
    CadLayer EffectiveLayer,
    CadColor ReferenceColor);
