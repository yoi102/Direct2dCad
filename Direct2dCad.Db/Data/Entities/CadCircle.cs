using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadCircle : Curve
{
    public CadPointD Center { get; private set; }
    public double Radius { get; private set; }

    public override bool IsClosed => true;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    public override double Length => 2 * Math.PI * Radius;

    public override CadRectD Bounds => CadRectD.FromLTRB(
        Center.X - Radius,
        Center.Y - Radius,
        Center.X + Radius,
        Center.Y + Radius);

    internal CadCircle(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD center,
        double radius,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Center = center;
        Radius = GuardRadius(radius);
    }

    public void SetCenter(CadPointD center) => Center = center;

    public void SetRadius(double radius) => Radius = GuardRadius(radius);

    public void SetGeometry(CadPointD center, double radius)
    {
        Center = center;
        Radius = GuardRadius(radius);
    }

    private static double GuardRadius(double radius)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? throw new ArgumentOutOfRangeException(nameof(radius))
            : radius;
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;
}
