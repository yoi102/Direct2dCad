namespace Direct2dCad.Rendering.Direct2D.Scene;

internal static class Direct2DRenderScaleBucket
{
    // A bucket differs from its neighbours by about 4.4%, with at most about 2.2%
    // resampling inside one bucket. This avoids rebuilding retained content for every
    // high-resolution wheel delta while keeping raster and screen-width drift subtle.
    private const int ProfilesPerOctave = 16;

    public static double Quantize(double scale)
    {
        scale = Math.Max(scale, 1e-9);
        return Math.Pow(
            2.0,
            Math.Round(Math.Log2(scale) * ProfilesPerOctave) / ProfilesPerOctave);
    }
}
