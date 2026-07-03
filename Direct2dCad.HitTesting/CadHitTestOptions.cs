namespace Direct2dCad.HitTesting;

public sealed class CadHitTestOptions
{
    public double ViewportZoom { get; init; } = 1.0;
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;

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
            ? modelStrokeWidth / zoom
            : modelStrokeWidth;

        return Math.Max(strokeWidth, MinimumScreenStrokeWidth / zoom);
    }
}
