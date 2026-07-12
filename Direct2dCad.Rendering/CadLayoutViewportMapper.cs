using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public static class CadLayoutViewportMapper
{
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
