using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadRectangle : Curve
{
    private CadRectD _bounds;
    private double _cornerRadiusX;
    private double _cornerRadiusY;

    public override bool IsClosed => true;

    public override double Length
    {
        get
        {
            if (!HasRoundedCorners)
                return 2 * (Bounds.Width + Bounds.Height);

            var straightLength = 2 * (Bounds.Width - 2 * CornerRadiusX) +
                                 2 * (Bounds.Height - 2 * CornerRadiusY);
            return straightLength + EstimateEllipsePerimeter(CornerRadiusX, CornerRadiusY);
        }
    }

    public override CadRectD Bounds => _bounds;

    public double CornerRadiusX => _cornerRadiusX;

    public double CornerRadiusY => _cornerRadiusY;

    public bool HasRoundedCorners => CornerRadiusX > 0 && CornerRadiusY > 0;

    public StyleId? GraphicStyleId { get; private set; }

    public StyleId? FillStyleId { get; private set; }

    internal CadRectangle(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadRectD bounds,
        double cornerRadiusX = 0,
        double cornerRadiusY = 0,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
        SetCornerRadius(cornerRadiusX, cornerRadiusY);
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
        NormalizeCornerRadius();
    }

    public void SetGeometry(CadPointD firstCorner, CadPointD oppositeCorner)
    {
        SetBounds(CadRectD.FromLTRB(
            firstCorner.X,
            firstCorner.Y,
            oppositeCorner.X,
            oppositeCorner.Y));
    }

    public void SetCornerRadius(double radiusX, double radiusY)
    {
        _cornerRadiusX = GuardRadius(radiusX, nameof(radiusX));
        _cornerRadiusY = GuardRadius(radiusY, nameof(radiusY));
        NormalizeCornerRadius();
    }

    public void SetCornerRadiusX(double radiusX)
    {
        _cornerRadiusX = GuardRadius(radiusX, nameof(radiusX));
        NormalizeCornerRadius();
    }

    public void SetCornerRadiusY(double radiusY)
    {
        _cornerRadiusY = GuardRadius(radiusY, nameof(radiusY));
        NormalizeCornerRadius();
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

    private void NormalizeCornerRadius()
    {
        if (_bounds.IsEmpty)
        {
            _cornerRadiusX = 0;
            _cornerRadiusY = 0;
            return;
        }

        _cornerRadiusX = Math.Clamp(_cornerRadiusX, 0, _bounds.Width * 0.5);
        _cornerRadiusY = Math.Clamp(_cornerRadiusY, 0, _bounds.Height * 0.5);
    }

    private static double GuardRadius(double radius, string paramName)
    {
        return radius < 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? throw new ArgumentOutOfRangeException(paramName)
            : radius;
    }

    private static double EstimateEllipsePerimeter(double radiusX, double radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
            return 0;

        var a = Math.Max(radiusX, radiusY);
        var b = Math.Min(radiusX, radiusY);
        var h = Math.Pow(a - b, 2) / Math.Pow(a + b, 2);
        return Math.PI * (a + b) * (1 + (3 * h) / (10 + Math.Sqrt(4 - 3 * h)));
    }
}
