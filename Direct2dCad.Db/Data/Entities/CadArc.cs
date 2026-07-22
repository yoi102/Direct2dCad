using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

/// <summary>
/// 圆弧实体。
/// 角度统一使用 radians。
/// SweepAngleRadians > 0 表示逆时针，SweepAngleRadians < 0 表示顺时针。
/// </summary>
public sealed class CadArc : Curve
{
    private const double TwoPi = Math.PI * 2.0;
    private const double Epsilon = 1e-12;
    private CadPointD _startPoint;
    private CadPointD _endPoint;
    private CadRectD _bounds;

    public CadPointD Center { get; private set; }
    public double Radius { get; private set; }

    /// <summary>
    /// 起始角，单位：radians。
    /// </summary>
    public double StartAngleRadians { get; private set; }

    /// <summary>
    /// 扫掠角，单位：radians。
    /// 正数表示逆时针，负数表示顺时针。
    /// </summary>
    public double SweepAngleRadians { get; private set; }

    public double EndAngleRadians => StartAngleRadians + SweepAngleRadians;

    public CadPointD StartPoint => _startPoint;
    public CadPointD EndPoint => _endPoint;

    public bool IsClockwise => SweepAngleRadians < 0;
    public bool IsCounterClockwise => SweepAngleRadians > 0;
    public bool IsFullCircle => Math.Abs(Math.Abs(SweepAngleRadians) - TwoPi) <= Epsilon;
    public StyleId? GraphicStyleId { get; private set; }
    public override bool IsClosed => false;
    public override double Length => Radius * Math.Abs(SweepAngleRadians);

    public override CadCurveOrientation Orientation =>
        SweepAngleRadians < 0
            ? CadCurveOrientation.Clockwise
            : CadCurveOrientation.CounterClockwise;

    public override CadRectD Bounds => _bounds;

    internal CadArc(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Center = center;
        Radius = GuardRadius(radius);
        StartAngleRadians = GuardAngle(startAngleRadians, nameof(startAngleRadians));
        SweepAngleRadians = GuardSweepAngle(sweepAngleRadians);
        RebuildDerivedGeometry();
    }

    public static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    public static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    public double StartAngleDegrees => RadiansToDegrees(StartAngleRadians);
    public double SweepAngleDegrees => RadiansToDegrees(SweepAngleRadians);
    public double EndAngleDegrees => RadiansToDegrees(EndAngleRadians);

    public CadPointD GetPointAtAngle(double angleRadians)
    {
        return new CadPointD(
            Center.X + Math.Cos(angleRadians) * Radius,
            Center.Y + Math.Sin(angleRadians) * Radius);
    }

    public void SetCenter(CadPointD center)
    {
        Center = center;
        RebuildDerivedGeometry();
    }

    public void SetRadius(double radius)
    {
        Radius = GuardRadius(radius);
        RebuildDerivedGeometry();
    }

    public void SetAngles(double startAngleRadians, double sweepAngleRadians)
    {
        StartAngleRadians = GuardAngle(startAngleRadians, nameof(startAngleRadians));
        SweepAngleRadians = GuardSweepAngle(sweepAngleRadians);
        RebuildDerivedGeometry();
    }

    public void SetGeometry(
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        Center = center;
        Radius = GuardRadius(radius);
        StartAngleRadians = GuardAngle(startAngleRadians, nameof(startAngleRadians));
        SweepAngleRadians = GuardSweepAngle(sweepAngleRadians);
        RebuildDerivedGeometry();
    }

    public void SetGeometryDegrees(
        CadPointD center,
        double radius,
        double startAngleDegrees,
        double sweepAngleDegrees)
    {
        SetGeometry(
            center,
            radius,
            DegreesToRadians(startAngleDegrees),
            DegreesToRadians(sweepAngleDegrees));
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    private CadRectD CalculateBounds()
    {
        if (IsFullCircle)
        {
            return CadRectD.FromLTRB(
                Center.X - Radius,
                Center.Y - Radius,
                Center.X + Radius,
                Center.Y + Radius);
        }

        var bounds = CadRectD.Empty
            .ExpandToInclude(StartPoint)
            .ExpandToInclude(EndPoint);

        var cardinalAngles = new[]
        {
            0.0,
            Math.PI * 0.5,
            Math.PI,
            Math.PI * 1.5
        };

        foreach (var angle in cardinalAngles)
        {
            if (ContainsAngle(angle))
                bounds = bounds.ExpandToInclude(GetPointAtAngle(angle));
        }

        return bounds;
    }

    private void RebuildDerivedGeometry()
    {
        _startPoint = GetPointAtAngle(StartAngleRadians);
        _endPoint = GetPointAtAngle(EndAngleRadians);
        _bounds = CalculateBounds();
    }

    private bool ContainsAngle(double angleRadians)
    {
        var start = NormalizeAngle(StartAngleRadians);
        var target = NormalizeAngle(angleRadians);
        var sweep = SweepAngleRadians;

        if (sweep > 0)
        {
            var delta = NormalizePositive(target - start);
            return delta <= sweep + Epsilon;
        }
        else
        {
            var delta = NormalizePositive(start - target);
            return delta <= -sweep + Epsilon;
        }
    }

    private static double NormalizeAngle(double radians)
    {
        var result = radians % TwoPi;

        if (result < 0)
            result += TwoPi;

        return result;
    }

    private static double NormalizePositive(double radians)
    {
        var result = radians % TwoPi;

        if (result < 0)
            result += TwoPi;

        return result;
    }

    private static double GuardRadius(double radius)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? throw new ArgumentOutOfRangeException(nameof(radius))
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
            throw new ArgumentOutOfRangeException(nameof(sweepAngle), "Sweep angle cannot be greater than 2π.");

        return sweepAngle;
    }
}
