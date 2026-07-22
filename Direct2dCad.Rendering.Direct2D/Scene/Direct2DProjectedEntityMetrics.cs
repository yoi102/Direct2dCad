using System.Numerics;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal readonly struct Direct2DProjectedEntityMetrics
{
    public double ScreenWidth { get; }
    public double ScreenHeight { get; }
    public double MinimumExtent { get; }
    public double MaximumExtent { get; }
    public double ProjectedArea { get; }
    public double ScreenStrokeWidth { get; }

    private Direct2DProjectedEntityMetrics(
        double screenWidth,
        double screenHeight,
        double projectedArea,
        double screenStrokeWidth)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        MinimumExtent = Math.Min(screenWidth, screenHeight);
        MaximumExtent = Math.Max(screenWidth, screenHeight);
        ProjectedArea = projectedArea;
        ScreenStrokeWidth = screenStrokeWidth;
    }

    public static bool TryCreate(
        CadRectD bounds,
        double screenScale,
        double screenStrokeWidth,
        out Direct2DProjectedEntityMetrics metrics)
    {
        metrics = default;
        if (bounds.IsEmpty ||
            !double.IsFinite(screenScale) ||
            screenScale <= double.Epsilon ||
            !double.IsFinite(screenStrokeWidth))
        {
            return false;
        }

        var modelWidth = Math.Abs(bounds.Width);
        var modelHeight = Math.Abs(bounds.Height);
        var screenWidth = modelWidth * screenScale;
        var screenHeight = modelHeight * screenScale;
        var projectedArea = screenWidth * screenHeight;
        return TryCreateCore(
            screenWidth,
            screenHeight,
            projectedArea,
            screenStrokeWidth,
            out metrics);
    }

    public static bool TryCreate(
        CadRectD bounds,
        Matrix3x2 transform,
        double transformScaleMultiplier,
        double screenStrokeWidth,
        out Direct2DProjectedEntityMetrics metrics)
    {
        metrics = default;
        if (bounds.IsEmpty ||
            !double.IsFinite(transformScaleMultiplier) ||
            transformScaleMultiplier <= double.Epsilon ||
            !double.IsFinite(screenStrokeWidth))
        {
            return false;
        }

        var modelWidth = Math.Abs(bounds.Width);
        var modelHeight = Math.Abs(bounds.Height);
        var screenWidth = (
            modelWidth * Math.Abs(transform.M11) +
            modelHeight * Math.Abs(transform.M21)) * transformScaleMultiplier;
        var screenHeight = (
            modelWidth * Math.Abs(transform.M12) +
            modelHeight * Math.Abs(transform.M22)) * transformScaleMultiplier;
        var determinant = Math.Abs(
            (double)transform.M11 * transform.M22 -
            (double)transform.M12 * transform.M21);
        var projectedArea = modelWidth * modelHeight * determinant *
                            transformScaleMultiplier * transformScaleMultiplier;
        return TryCreateCore(
            screenWidth,
            screenHeight,
            projectedArea,
            screenStrokeWidth,
            out metrics);
    }

    private static bool TryCreateCore(
        double screenWidth,
        double screenHeight,
        double projectedArea,
        double screenStrokeWidth,
        out Direct2DProjectedEntityMetrics metrics)
    {
        metrics = default;
        if (!double.IsFinite(screenWidth) ||
            !double.IsFinite(screenHeight) ||
            !double.IsFinite(projectedArea) ||
            screenWidth < 0.0 ||
            screenHeight < 0.0 ||
            projectedArea < 0.0)
        {
            return false;
        }

        metrics = new Direct2DProjectedEntityMetrics(
            screenWidth,
            screenHeight,
            projectedArea,
            Math.Max(0.0, screenStrokeWidth));
        return true;
    }
}
