using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadEllipseArc : Curve
{
    private const double TwoPi = Math.PI * 2.0;
    private const double Epsilon = 1e-12;

    public CadPointD Center { get; private set; }
    public double RadiusX { get; private set; }
    public double RadiusY { get; private set; }
    public double StartAngleRadians { get; private set; }
    public double SweepAngleRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }

    public double EndAngleRadians => StartAngleRadians + SweepAngleRadians;
    public CadPointD StartPoint => GetPointAtAngle(StartAngleRadians);
    public CadPointD EndPoint => GetPointAtAngle(EndAngleRadians);
    public override bool IsClosed => false;
    public override double Length => ApproximateLength();

    public override CadCurveOrientation Orientation =>
        SweepAngleRadians < 0
            ? CadCurveOrientation.Clockwise
            : CadCurveOrientation.CounterClockwise;

    public override CadRectD Bounds => CalculateBounds();

    internal CadEllipseArc(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Center = center;
        RadiusX = GuardRadius(radiusX, nameof(radiusX));
        RadiusY = GuardRadius(radiusY, nameof(radiusY));
        StartAngleRadians = GuardAngle(startAngleRadians, nameof(startAngleRadians));
        SweepAngleRadians = GuardSweepAngle(sweepAngleRadians);
    }

    public double StartAngleDegrees => CadArc.RadiansToDegrees(StartAngleRadians);
    public double SweepAngleDegrees => CadArc.RadiansToDegrees(SweepAngleRadians);
    public double EndAngleDegrees => CadArc.RadiansToDegrees(EndAngleRadians);

    public CadPointD GetPointAtAngle(double angleRadians)
    {
        return new CadPointD(
            Center.X + Math.Cos(angleRadians) * RadiusX,
            Center.Y + Math.Sin(angleRadians) * RadiusY);
    }

    public void SetCenter(CadPointD center) => Center = center;

    public void SetGeometry(
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        Center = center;
        RadiusX = GuardRadius(radiusX, nameof(radiusX));
        RadiusY = GuardRadius(radiusY, nameof(radiusY));
        StartAngleRadians = GuardAngle(startAngleRadians, nameof(startAngleRadians));
        SweepAngleRadians = GuardSweepAngle(sweepAngleRadians);
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    private CadRectD CalculateBounds()
    {
        var bounds = CadRectD.Empty
            .ExpandToInclude(StartPoint)
            .ExpandToInclude(EndPoint);

        foreach (var angle in new[] { 0.0, Math.PI * 0.5, Math.PI, Math.PI * 1.5 })
        {
            if (ContainsAngle(angle))
                bounds = bounds.ExpandToInclude(GetPointAtAngle(angle));
        }

        return bounds;
    }

    private bool ContainsAngle(double angleRadians)
    {
        var start = NormalizeAngle(StartAngleRadians);
        var target = NormalizeAngle(angleRadians);

        if (SweepAngleRadians > 0)
            return NormalizePositive(target - start) <= SweepAngleRadians + Epsilon;

        return NormalizePositive(start - target) <= -SweepAngleRadians + Epsilon;
    }

    private double ApproximateLength()
    {
        var steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(SweepAngleRadians) / (Math.PI / 24.0)));
        var length = 0.0;
        var previous = StartPoint;

        for (var i = 1; i <= steps; i++)
        {
            var angle = StartAngleRadians + SweepAngleRadians * i / steps;
            var next = GetPointAtAngle(angle);
            length += previous.DistanceTo(next);
            previous = next;
        }

        return length;
    }

    private static double NormalizeAngle(double radians)
    {
        var result = radians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    private static double NormalizePositive(double radians)
    {
        var result = radians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    private static double GuardRadius(double radius, string paramName)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? throw new ArgumentOutOfRangeException(paramName)
            : radius;
    }

    private static double GuardAngle(double angle, string paramName)
    {
        return double.IsNaN(angle) || double.IsInfinity(angle)
            ? throw new ArgumentOutOfRangeException(paramName)
            : angle;
    }

    private static double GuardSweepAngle(double sweepAngle)
    {
        if (double.IsNaN(sweepAngle) || double.IsInfinity(sweepAngle))
            throw new ArgumentOutOfRangeException(nameof(sweepAngle));

        if (Math.Abs(sweepAngle) <= Epsilon)
            throw new ArgumentOutOfRangeException(nameof(sweepAngle), "Sweep angle cannot be zero.");

        if (Math.Abs(sweepAngle) - TwoPi > Epsilon)
            throw new ArgumentOutOfRangeException(nameof(sweepAngle), "Sweep angle cannot be greater than 2*pi.");

        return sweepAngle;
    }
}
