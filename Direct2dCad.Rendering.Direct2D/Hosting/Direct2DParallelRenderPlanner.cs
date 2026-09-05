using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Scene;
using System.Collections;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

internal static class Direct2DParallelRenderPlanner
{
    internal const int MaximumWorkerCount = 4;
    internal const int DefaultWorkerCount = 2;
    internal const int DefaultEntityThreshold = 1000;

    public static bool TryCreatePlan(
        CadDocument document,
        CadRenderOptions options,
        CadParallelRenderingMode expectedMode,
        IReadOnlyList<CadEntity> entities,
        int width,
        int height,
        out Direct2DParallelRenderPlan plan)
    {
        plan = default;
        var requestedWorkerCount = Math.Clamp(
            options.ParallelRenderingWorkerCount,
            2,
            MaximumWorkerCount);
        var threshold = Math.Max(2, options.ParallelRenderingEntityThreshold);
        if (!options.IsParallelRenderingEnabled ||
            options.ParallelRenderingMode != expectedMode ||
            options.ActiveLayoutId is not null ||
            entities.Count < threshold ||
            width <= 0 ||
            height <= 0 ||
            requestedWorkerCount <= 1 ||
            ContainsUnsupportedEntities(entities))
        {
            return false;
        }

        var activeWorkerCount = Math.Min(requestedWorkerCount, entities.Count);
        plan = new Direct2DParallelRenderPlan(
            activeWorkerCount,
            CreateBatches(document, entities, activeWorkerCount));
        return true;
    }

    private static Direct2DParallelRenderBatch[] CreateBatches(
        CadDocument document,
        IReadOnlyList<CadEntity> entities,
        int workerCount)
    {
        long remainingCost = 0;
        foreach (var entity in entities)
            remainingCost += Direct2DEntityOrderCache.EstimateEntityRenderWork(document, entity);
        var result = new Direct2DParallelRenderBatch[workerCount];
        var start = 0;
        for (var index = 0; index < workerCount; index++)
        {
            var workersLeft = workerCount - index;
            var targetCost = (double)remainingCost / workersLeft;
            var maximumCount = entities.Count - start - (workersLeft - 1);
            var count = 0;
            long cost = 0;
            while (count < maximumCount)
            {
                var next = Direct2DEntityOrderCache.EstimateEntityRenderWork(document, entities[start + count]);
                if (workersLeft > 1 && count > 0 &&
                    Math.Abs(cost - targetCost) <= Math.Abs(cost + next - targetCost))
                    break;
                cost += next;
                count++;
            }
            // Keep contiguous ranges: regrouping by type would break alpha/draw order.
            result[index] = new Direct2DParallelRenderBatch(
                entities,
                start,
                count);
            start += count;
            remainingCost -= cost;
        }

        return result;
    }

    private static bool ContainsUnsupportedEntities(IReadOnlyList<CadEntity> entities)
    {
        foreach (var entity in entities)
        {
            // OLE may call UI/COM callbacks. A block reference may recursively contain OLE.
            if (entity is CadOleObject or CadBlockReference)
                return true;
        }

        return false;
    }
}

internal readonly record struct Direct2DParallelRenderPlan(
    int WorkerCount,
    IReadOnlyList<Direct2DParallelRenderBatch> Batches);

internal sealed class Direct2DParallelRenderBatch : IReadOnlyList<CadEntity>
{
    private readonly IReadOnlyList<CadEntity> _source;
    private readonly int _start;

    public Direct2DParallelRenderBatch(
        IReadOnlyList<CadEntity> source,
        int start,
        int count)
    {
        _source = source;
        _start = start;
        Count = count;
    }

    public int Count { get; }

    public CadEntity this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _source[_start + index];
        }
    }

    public IEnumerator<CadEntity> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
            yield return _source[_start + index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal readonly record struct Direct2DParallelFrameMetrics(
    CadParallelRenderingMode Mode,
    int WorkerCount,
    int EntityCount,
    double ElapsedMilliseconds,
    IReadOnlyList<CadRenderStatistics> WorkerStatistics);
