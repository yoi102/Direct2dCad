using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadRectangle : Curve
{
    private CadRectD _bounds;

    public override bool IsClosed => true;

    public override double Length => 2 * (Bounds.Width + Bounds.Height);

    public override CadRectD Bounds => _bounds;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    internal CadRectangle(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadRectD bounds,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
    }

    public void SetGeometry(CadPointD firstCorner, CadPointD oppositeCorner)
    {
        SetBounds(CadRectD.FromLTRB(
            firstCorner.X,
            firstCorner.Y,
            oppositeCorner.X,
            oppositeCorner.Y));
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;

    private static CadRectD GuardBounds(CadRectD bounds)
    {
        return bounds.IsEmpty ||
               bounds.Width <= 0 ||
               bounds.Height <= 0 ||
               double.IsNaN(bounds.Width) ||
               double.IsNaN(bounds.Height) ||
               double.IsInfinity(bounds.Width) ||
               double.IsInfinity(bounds.Height)
            ? throw new ArgumentOutOfRangeException(nameof(bounds))
            : bounds;
    }
}
