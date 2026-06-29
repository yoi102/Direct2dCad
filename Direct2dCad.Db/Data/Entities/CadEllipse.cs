using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadEllipse : Curve
{
    public CadPointD Center { get; private set; }
    public double RadiusX { get; private set; }
    public double RadiusY { get; private set; }

    public override bool IsClosed => true;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    public override double Length
    {
        get
        {
            var a = Math.Max(RadiusX, RadiusY);
            var b = Math.Min(RadiusX, RadiusY);
            var h = Math.Pow(a - b, 2) / Math.Pow(a + b, 2);
            return Math.PI * (a + b) * (1 + (3 * h) / (10 + Math.Sqrt(4 - 3 * h)));
        }
    }

    public override CadRectD Bounds => CadRectD.FromLTRB(
        Center.X - RadiusX,
        Center.Y - RadiusY,
        Center.X + RadiusX,
        Center.Y + RadiusY);

    internal CadEllipse(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD center,
        double radiusX,
        double radiusY,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Center = center;
        RadiusX = GuardRadius(radiusX, nameof(radiusX));
        RadiusY = GuardRadius(radiusY, nameof(radiusY));
    }

    public void SetCenter(CadPointD center) => Center = center;

    public void SetRadiusX(double radiusX) => RadiusX = GuardRadius(radiusX, nameof(radiusX));

    public void SetRadiusY(double radiusY) => RadiusY = GuardRadius(radiusY, nameof(radiusY));

    public void SetGeometry(CadPointD center, double radiusX, double radiusY)
    {
        Center = center;
        RadiusX = GuardRadius(radiusX, nameof(radiusX));
        RadiusY = GuardRadius(radiusY, nameof(radiusY));
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    public void SetFillStyleInternal(StyleId? styleId) => FillStyleId = styleId;

    private static double GuardRadius(double radius, string paramName)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? throw new ArgumentOutOfRangeException(paramName)
            : radius;
    }
}
