using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal static class Direct2DAdaptiveChunkPlanner
{
    private const double MinimumCoverage = 0.08;
    private const double MinimumExtentGrowth = 1.35;
    private const double LineFootprintRatio = 0.02;

    public static bool ShouldFlushBefore(
        int currentCount,
        int minimumSpatialCount,
        int maximumCount,
        CadRectD currentBounds,
        double currentFootprint,
        CadRectD nextBounds)
    {
        if (currentCount >= maximumCount)
            return true;
        if (currentCount < minimumSpatialCount ||
            currentBounds.IsEmpty ||
            nextBounds.IsEmpty)
        {
            return false;
        }

        var combinedBounds = currentBounds.Union(nextBounds);
        var currentExtent = EstimateFootprint(currentBounds);
        var combinedExtent = EstimateFootprint(combinedBounds);
        if (currentExtent <= double.Epsilon ||
            combinedExtent < currentExtent * MinimumExtentGrowth)
        {
            return false;
        }

        var occupied = currentFootprint + EstimateFootprint(nextBounds);
        return occupied / combinedExtent < MinimumCoverage;
    }

    public static double EstimateFootprint(CadRectD bounds)
    {
        if (bounds.IsEmpty)
            return 0;

        var width = Math.Max(bounds.Width, 0);
        var height = Math.Max(bounds.Height, 0);
        var major = Math.Max(width, height);
        if (major <= double.Epsilon)
            return double.Epsilon;

        var minor = Math.Max(Math.Min(width, height), major * LineFootprintRatio);
        return major * minor;
    }
}
