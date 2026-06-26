using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public sealed class CadViewport
{
    public double Zoom { get; private set; } = 1.0;

    public CadPointD Offset { get; private set; } = CadPointD.Origin;

    public double ViewWidth { get; private set; }

    public double ViewHeight { get; private set; }

    public CadRectD VisibleWorldBounds { get; private set; }

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
}
