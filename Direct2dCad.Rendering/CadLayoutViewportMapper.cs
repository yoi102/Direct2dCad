using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public static class CadLayoutViewportMapper
{
    public static CadRectD PaperToModelBounds(CadLayoutViewport viewport, CadRectD bounds) =>
        bounds.IsEmpty ? CadRectD.Empty : TransformBounds(
            bounds, PaperToModel(viewport, bounds.Center), 1.0 / viewport.Scale, viewport.RotationRadians);

    public static CadRectD ModelToPaperBounds(CadLayoutViewport viewport, CadRectD bounds) =>
        bounds.IsEmpty ? CadRectD.Empty : TransformBounds(
            bounds, ModelToPaper(viewport, bounds.Center), viewport.Scale, viewport.RotationRadians);

    private static CadRectD TransformBounds(CadRectD bounds, CadPointD center, double scale, double rotation)
    {
        var cos = Math.Abs(Math.Cos(rotation));
        var sin = Math.Abs(Math.Sin(rotation));
        return CadRectD.FromCenter(center,
            (bounds.Width * cos + bounds.Height * sin) * scale,
            (bounds.Width * sin + bounds.Height * cos) * scale);
    }

    public static CadPointD PaperToModel(CadLayoutViewport viewport, CadPointD paperPoint)
    {
        var dx = (paperPoint.X - viewport.Bounds.Center.X) / viewport.Scale;
        var dy = (paperPoint.Y - viewport.Bounds.Center.Y) / viewport.Scale;
        var cos = Math.Cos(viewport.RotationRadians);
        var sin = Math.Sin(viewport.RotationRadians);
        return new CadPointD(
            viewport.ModelCenter.X + dx * cos + dy * sin,
            viewport.ModelCenter.Y - dx * sin + dy * cos);
    }

    public static CadPointD ModelToPaper(CadLayoutViewport viewport, CadPointD modelPoint)
    {
        var dx = modelPoint.X - viewport.ModelCenter.X;
        var dy = modelPoint.Y - viewport.ModelCenter.Y;
        var cos = Math.Cos(viewport.RotationRadians);
        var sin = Math.Sin(viewport.RotationRadians);
        return new CadPointD(
            viewport.Bounds.Center.X + (dx * cos - dy * sin) * viewport.Scale,
            viewport.Bounds.Center.Y + (dx * sin + dy * cos) * viewport.Scale);
    }

    public static CadPointD ScreenToModel(
        CadViewport paperViewport,
        CadLayoutViewport layoutViewport,
        CadPointD screenPoint) =>
        PaperToModel(layoutViewport, paperViewport.ScreenToWorld(screenPoint));

    public static CadPointD ModelToScreen(
        CadViewport paperViewport,
        CadLayoutViewport layoutViewport,
        CadPointD modelPoint) =>
        paperViewport.WorldToScreen(ModelToPaper(layoutViewport, modelPoint));
}
