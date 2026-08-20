using Direct2dCad.Rendering;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

internal sealed class Direct2DDirtyRegionPlanner
{
    private const double PartialRenderMaxAreaRatio = 0.65;
    private const int CostOptimizationThreshold = 2;
    private const int PairwiseOptimizationLimit = 8;
    private const double MergeCostTolerance = 1.15;
    private const double MaximumMergeAreaRatio = 2.0;
    private const double CombinedMaximumAreaRatio = 8.0;
    private const double CombinedMaximumTargetAreaRatio = 0.5;
    private const double CombinedCostTolerance = 1.05;
    private readonly List<CadScreenRect> _rects = new(8);
    private readonly List<double> _costs = new(8);

    public CadRenderInvalidation Normalize(
        CadRenderInvalidation? invalidation,
        int targetWidth,
        int targetHeight,
        Func<CadScreenRect, double> estimateCost)
    {
        ArgumentNullException.ThrowIfNull(estimateCost);
        if (invalidation is null || invalidation.IsFull)
            return CadRenderInvalidation.Full;
        if (targetWidth <= 0 || targetHeight <= 0)
            return CadRenderInvalidation.Full;

        _rects.Clear();
        if (_rects.Capacity < invalidation.DirtyScreenRects.Count)
            _rects.Capacity = invalidation.DirtyScreenRects.Count;

        foreach (var dirtyRect in invalidation.DirtyScreenRects)
        {
            var rect = ClampToTarget(dirtyRect, targetWidth, targetHeight);
            if (!rect.IsEmpty)
                _rects.Add(rect);
        }

        if (_rects.Count == 0)
            return CadRenderInvalidation.Empty;

        var normalized = CadRenderInvalidation.FromScreenRectsPreservingCoverage(_rects);
        if (normalized.DirtyScreenRects.Count >= CostOptimizationThreshold)
            normalized = Optimize(normalized, estimateCost);

        long area = 0;
        foreach (var rect in normalized.DirtyScreenRects)
            area += rect.Area;

        var targetArea = (double)targetWidth * targetHeight;
        return targetArea > 0 && area / targetArea >= PartialRenderMaxAreaRatio
            ? CadRenderInvalidation.Full
            : normalized;
    }

    public bool TryGetCombinedBounds(
        CadRenderInvalidation invalidation,
        int targetWidth,
        int targetHeight,
        Func<CadScreenRect, double> estimateCost,
        out CadScreenRect combinedBounds)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        ArgumentNullException.ThrowIfNull(estimateCost);

        combinedBounds = invalidation.DirtyScreenRect;
        if (invalidation.IsFull ||
            invalidation.DirtyScreenRects.Count < 2 ||
            combinedBounds.IsEmpty)
        {
            return false;
        }

        long dirtyArea = 0;
        var separateCost = 0.0;
        foreach (var rect in invalidation.DirtyScreenRects)
        {
            dirtyArea += rect.Area;
            separateCost += estimateCost(rect);
        }

        var targetArea = Math.Max(1.0, (double)targetWidth * targetHeight);
        if (dirtyArea <= 0 ||
            combinedBounds.Area > dirtyArea * CombinedMaximumAreaRatio ||
            combinedBounds.Area > targetArea * CombinedMaximumTargetAreaRatio)
        {
            return false;
        }

        return estimateCost(combinedBounds) <= separateCost * CombinedCostTolerance;
    }

    private CadRenderInvalidation Optimize(
        CadRenderInvalidation invalidation,
        Func<CadScreenRect, double> estimateCost)
    {
        _rects.Clear();
        _costs.Clear();
        foreach (var rect in invalidation.DirtyScreenRects)
        {
            _rects.Add(rect);
            _costs.Add(estimateCost(rect));
        }

        if (_rects.Count > PairwiseOptimizationLimit)
            CompactToPairwiseLimit(estimateCost);

        while (_rects.Count > 1)
        {
            var bestLeft = -1;
            var bestRight = -1;
            var bestUnion = default(CadScreenRect);
            var bestUnionCost = 0.0;
            var bestSaving = double.NegativeInfinity;

            for (var left = 0; left < _rects.Count - 1; left++)
            {
                for (var right = left + 1; right < _rects.Count; right++)
                {
                    var sourceCost = _costs[left] + _costs[right];
                    var union = _rects[left].Union(_rects[right]);
                    var sourceArea = _rects[left].Area + _rects[right].Area;
                    if (union.Area > sourceArea * MaximumMergeAreaRatio)
                        continue;

                    var unionCost = estimateCost(union);
                    if (unionCost > sourceCost * MergeCostTolerance)
                        continue;

                    var saving = sourceCost - unionCost;
                    if (saving <= bestSaving)
                        continue;

                    bestLeft = left;
                    bestRight = right;
                    bestUnion = union;
                    bestUnionCost = unionCost;
                    bestSaving = saving;
                }
            }

            if (bestLeft < 0)
                break;

            ReplacePair(bestLeft, bestRight, bestUnion, bestUnionCost);
        }

        return CadRenderInvalidation.FromScreenRectsPreservingCoverage(_rects);
    }

    private void CompactToPairwiseLimit(Func<CadScreenRect, double> estimateCost)
    {
        while (_rects.Count > PairwiseOptimizationLimit)
        {
            var bestLeft = -1;
            var bestRight = -1;
            var bestUnion = default(CadScreenRect);
            var bestWaste = long.MaxValue;
            for (var left = 0; left < _rects.Count - 1; left++)
            {
                for (var right = left + 1; right < _rects.Count; right++)
                {
                    var first = _rects[left];
                    var second = _rects[right];
                    var sourceArea = first.Area + second.Area;
                    var union = first.Union(second);
                    if (union.Area > sourceArea * MaximumMergeAreaRatio)
                        continue;

                    var waste = Math.Max(0, union.Area - sourceArea);
                    if (waste > bestWaste ||
                        waste == bestWaste && union.Area >= bestUnion.Area)
                    {
                        continue;
                    }

                    bestLeft = left;
                    bestRight = right;
                    bestUnion = union;
                    bestWaste = waste;
                }
            }

            if (bestLeft < 0)
                return;

            var sourceCost = _costs[bestLeft] + _costs[bestRight];
            var unionCost = estimateCost(bestUnion);
            if (unionCost > sourceCost * MergeCostTolerance)
                return;

            ReplacePair(bestLeft, bestRight, bestUnion, unionCost);
        }
    }

    private void ReplacePair(
        int left,
        int right,
        CadScreenRect union,
        double unionCost)
    {
        _rects[left] = union;
        _costs[left] = unionCost;
        _rects.RemoveAt(right);
        _costs.RemoveAt(right);
    }

    private static CadScreenRect ClampToTarget(
        CadScreenRect rect,
        int targetWidth,
        int targetHeight)
    {
        if (rect.IsEmpty || targetWidth <= 0 || targetHeight <= 0)
            return default;

        var x = Math.Clamp(rect.X, 0, targetWidth);
        var y = Math.Clamp(rect.Y, 0, targetHeight);
        var right = (int)Math.Clamp((long)rect.X + rect.Width, 0L, targetWidth);
        var bottom = (int)Math.Clamp((long)rect.Y + rect.Height, 0L, targetHeight);
        return new CadScreenRect(x, y, right - x, bottom - y);
    }
}
