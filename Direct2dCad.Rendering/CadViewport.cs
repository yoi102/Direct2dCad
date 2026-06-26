using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public sealed class CadViewport
{
    private const double MinZoom = 1e-6;
    private const double MaxZoom = 1e6;

    public double Zoom { get; private set; } = 1.0;

    public CadPointD Offset { get; private set; } = CadPointD.Origin;

    public double ViewWidth { get; private set; }

    public double ViewHeight { get; private set; }

    public CadRectD VisibleWorldBounds { get; private set; }

    public void SetSize(double width, double height)
    {
        ViewWidth = GuardNonNegative(width, nameof(width));
        ViewHeight = GuardNonNegative(height, nameof(height));
        UpdateVisibleWorldBounds();
    }

    public void SetView(double zoom, CadPointD offset)
    {
        Zoom = GuardZoom(zoom);
        Offset = offset;
        UpdateVisibleWorldBounds();
    }

    public void PanScreen(CadVectorD screenDelta)
    {
        Offset += screenDelta;
        UpdateVisibleWorldBounds();
    }

    public void PanWorld(CadVectorD worldDelta)
    {
        Offset += new CadVectorD(worldDelta.X * Zoom, worldDelta.Y * Zoom);
        UpdateVisibleWorldBounds();
    }

    public void ZoomAt(CadPointD screenAnchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            throw new ArgumentOutOfRangeException(nameof(factor));

        var worldAnchor = ScreenToWorld(screenAnchor);
        Zoom = GuardZoom(Zoom * factor);
        Offset = new CadPointD(
            screenAnchor.X - worldAnchor.X * Zoom,
            screenAnchor.Y - worldAnchor.Y * Zoom);
        UpdateVisibleWorldBounds();
    }

    public CadPointD ScreenToWorld(CadPointD screen)
    {
        return new CadPointD(
            (screen.X - Offset.X) / Zoom,
            (screen.Y - Offset.Y) / Zoom);
    }

    public CadPointD WorldToScreen(CadPointD world)
    {
        return new CadPointD(
            world.X * Zoom + Offset.X,
            world.Y * Zoom + Offset.Y);
    }

    private void UpdateVisibleWorldBounds()
    {
        VisibleWorldBounds = CadRectD.Empty
            .ExpandToInclude(ScreenToWorld(CadPointD.Origin))
            .ExpandToInclude(ScreenToWorld(new CadPointD(ViewWidth, ViewHeight)));
    }

    private static double GuardZoom(double zoom)
    {
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom));

        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    private static double GuardNonNegative(double value, string paramName)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName);

        return value;
    }
}
