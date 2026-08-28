namespace Direct2dCad.HitTesting;

public sealed class CadHitTestOptions
{
    public const double DefaultScreenUnitsPerMillimeter = 96.0 / 25.4;

    public double ViewportZoom { get; init; } = 1.0;
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;
    public double LineWeightScreenUnitsPerMillimeter { get; init; } =
        DefaultScreenUnitsPerMillimeter;
    public double EntityLineWeightWorldScale { get; init; } = 1.0;

    public static CadHitTestOptions Default { get; } = new();

    public CadHitTestOptions()
    {
    }

    public CadHitTestOptions(double viewportZoom)
    {
        ViewportZoom = viewportZoom;
    }

    internal double ResolveWorldStrokeWidth(double modelStrokeWidth)
    {
        var zoom = Math.Max(ViewportZoom, double.Epsilon);
        var strokeWidth = KeepStrokeWidthScreenConstant
            ? modelStrokeWidth * ResolvePositiveFinite(
                LineWeightScreenUnitsPerMillimeter,
                DefaultScreenUnitsPerMillimeter) / zoom
            : modelStrokeWidth * ResolvePositiveFinite(EntityLineWeightWorldScale, 1.0);

        return Math.Max(
            strokeWidth,
            Math.Max(MinimumScreenStrokeWidth, 0.0) / zoom);
    }

    private static double ResolvePositiveFinite(double value, double fallback) =>
        double.IsFinite(value) && value > double.Epsilon ? value : fallback;
}
