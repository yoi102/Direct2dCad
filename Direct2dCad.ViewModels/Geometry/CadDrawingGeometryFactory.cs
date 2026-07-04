using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Geometry;

internal readonly record struct ArcDrawingGeometry(
    CadPointD Center,
    double Radius,
    double StartAngleRadians,
    double SweepAngleRadians);

internal readonly record struct EllipseDrawingGeometry(
    CadPointD Center,
    double RadiusX,
    double RadiusY);

internal readonly record struct EllipseArcDrawingGeometry(
    CadPointD Center,
    double RadiusX,
    double RadiusY,
    double StartAngleRadians,
    double SweepAngleRadians);

internal static class CadDrawingGeometryFactory
{
    private const double TwoPi = Math.PI * 2.0;

    public static bool TryCreateArcGeometry(
        CadPointD center,
        CadPointD start,
        CadPointD end,
        out double radius,
        out double startAngleRadians,
        out double sweepAngleRadians)
    {
        radius = center.DistanceTo(start);
        startAngleRadians = AngleFrom(center, start);
        sweepAngleRadians = ResolveSweepAngle(startAngleRadians, AngleFrom(center, end), counterClockwise: true);
        return center.DistanceTo(end) > double.Epsilon &&
               IsValidArcGeometry(radius, sweepAngleRadians);
    }

    public static bool TryCreateArcFromMode(
        CadCanvasToolMode toolMode,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        out ArcDrawingGeometry geometry)
    {
        return toolMode switch
        {
            CadCanvasToolMode.ArcThreePoint =>
                TryCreateArcFromThreePoints(first, second, third, out geometry),
            CadCanvasToolMode.ArcStartCenterEnd =>
                TryCreateArcFromCenterStartEnd(second, first, third, out geometry),
            CadCanvasToolMode.ArcStartCenterAngle =>
                TryCreateArcFromCenterStartEnd(second, first, third, out geometry),
            CadCanvasToolMode.ArcStartCenterLength =>
                TryCreateArcFromCenterStartLength(second, first, third, out geometry),
            CadCanvasToolMode.ArcStartEndAngle =>
                TryCreateArcFromStartEndAngle(first, second, third, out geometry),
            CadCanvasToolMode.ArcStartEndDirection =>
                TryCreateArcFromStartEndTangent(first, second, third - first, out geometry),
            CadCanvasToolMode.ArcStartEndRadius =>
                TryCreateArcFromStartEndRadius(first, second, third, out geometry),
            CadCanvasToolMode.ArcCenterStartEnd =>
                TryCreateArcFromCenterStartEnd(first, second, third, out geometry),
            CadCanvasToolMode.ArcCenterStartAngle =>
                TryCreateArcFromCenterStartEnd(first, second, third, out geometry),
            CadCanvasToolMode.ArcCenterStartLength =>
                TryCreateArcFromCenterStartLength(first, second, third, out geometry),
            _ => FailArcGeometry(out geometry)
        };
    }

    public static bool TryCreateArcFromCenterStartEnd(
        CadPointD center,
        CadPointD start,
        CadPointD endDirection,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;

        if (!TryCreateArcGeometry(
            center,
            start,
            endDirection,
            out var radius,
            out var startAngleRadians,
            out var sweepAngleRadians))
        {
            return false;
        }

        geometry = new ArcDrawingGeometry(center, radius, startAngleRadians, sweepAngleRadians);
        return true;
    }

    public static bool TryCreateArcFromCenterStartLength(
        CadPointD center,
        CadPointD start,
        CadPointD lengthPoint,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;
        var radius = center.DistanceTo(start);
        var chordLength = start.DistanceTo(lengthPoint);
        if (radius <= double.Epsilon || chordLength <= double.Epsilon)
            return false;

        chordLength = Math.Min(chordLength, radius * 2.0);
        var ratio = Math.Clamp(chordLength / (radius * 2.0), 0.0, 1.0);
        var sweepAngleRadians = 2.0 * Math.Asin(ratio);
        if (!IsValidArcGeometry(radius, sweepAngleRadians))
            return false;

        geometry = new ArcDrawingGeometry(center, radius, AngleFrom(center, start), sweepAngleRadians);
        return true;
    }

    public static bool TryCreateArcFromThreePoints(
        CadPointD start,
        CadPointD through,
        CadPointD end,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;

        if (!TryCreateCircleFromThreePoints(start, through, end, out var center, out var radius))
            return false;

        var startAngle = AngleFrom(center, start);
        var throughAngle = AngleFrom(center, through);
        var endAngle = AngleFrom(center, end);
        var positiveSweep = ResolveSweepAngle(startAngle, endAngle, counterClockwise: true);
        var sweepAngle = IsAngleOnSweep(startAngle, positiveSweep, throughAngle)
            ? positiveSweep
            : ResolveSweepAngle(startAngle, endAngle, counterClockwise: false);
        if (!IsValidArcGeometry(radius, sweepAngle))
            return false;

        geometry = new ArcDrawingGeometry(center, radius, startAngle, sweepAngle);
        return true;
    }

    public static bool TryCreateArcFromStartEndAngle(
        CadPointD start,
        CadPointD end,
        CadPointD anglePoint,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;
        var chord = end - start;
        var chordLength = chord.Length;
        if (chordLength <= double.Epsilon)
            return false;

        var midpoint = Midpoint(start, end);
        var leftNormal = chord.Normalize().Perpendicular();
        var signedSagitta = (anglePoint - midpoint).Dot(leftNormal);
        if (Math.Abs(signedSagitta) <= double.Epsilon)
            return false;

        var sweepMagnitude = 4.0 * Math.Atan2(Math.Abs(signedSagitta), chordLength * 0.5);
        var sweepAngle = -Math.Sign(signedSagitta) * sweepMagnitude;
        return TryCreateArcFromStartEndSweep(start, end, sweepAngle, out geometry);
    }

    public static bool TryCreateArcFromStartEndRadius(
        CadPointD start,
        CadPointD end,
        CadPointD radiusPoint,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;
        var chord = end - start;
        var chordLength = chord.Length;
        if (chordLength <= double.Epsilon)
            return false;

        var midpoint = Midpoint(start, end);
        var leftNormal = chord.Normalize().Perpendicular();
        var side = (radiusPoint - midpoint).Dot(leftNormal);
        if (Math.Abs(side) <= double.Epsilon)
            side = -1.0;

        var radius = Math.Max(start.DistanceTo(radiusPoint), chordLength * 0.5);
        var ratio = Math.Clamp((chordLength * 0.5) / radius, 0.0, 1.0);
        var sweepMagnitude = 2.0 * Math.Asin(ratio);
        var sweepAngle = -Math.Sign(side) * sweepMagnitude;
        return TryCreateArcFromStartEndSweep(start, end, sweepAngle, out geometry);
    }

    public static bool TryCreateArcFromStartEndTangent(
        CadPointD start,
        CadPointD end,
        CadVectorD tangent,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;
        var chord = end - start;
        var tangentUnit = tangent.Normalize();
        if (chord.Length <= double.Epsilon || tangentUnit == CadVectorD.Zero)
            return false;

        var centerDirection = tangentUnit.Perpendicular();
        var denominator = 2.0 * chord.Dot(centerDirection);
        if (Math.Abs(denominator) <= 1e-9)
            return false;

        var signedRadius = chord.LengthSquared / denominator;
        var radius = Math.Abs(signedRadius);
        var center = start + centerDirection * signedRadius;
        var startAngle = AngleFrom(center, start);
        var endAngle = AngleFrom(center, end);
        var sweepAngle = signedRadius > 0
            ? ResolveSweepAngle(startAngle, endAngle, counterClockwise: true)
            : ResolveSweepAngle(startAngle, endAngle, counterClockwise: false);

        if (!IsValidArcGeometry(radius, sweepAngle))
            return false;

        geometry = new ArcDrawingGeometry(center, radius, startAngle, sweepAngle);
        return true;
    }

    public static bool TryCreateArcFromStartEndSweep(
        CadPointD start,
        CadPointD end,
        double sweepAngleRadians,
        out ArcDrawingGeometry geometry)
    {
        geometry = default;
        var chord = end - start;
        var chordLength = chord.Length;
        var sweepMagnitude = Math.Abs(sweepAngleRadians);
        if (chordLength <= double.Epsilon ||
            sweepMagnitude <= 1e-9 ||
            sweepMagnitude >= TwoPi - 1e-9)
        {
            return false;
        }

        var halfChord = chordLength * 0.5;
        var halfSweep = sweepMagnitude * 0.5;
        var sinHalfSweep = Math.Sin(halfSweep);
        var tanHalfSweep = Math.Tan(halfSweep);
        if (Math.Abs(sinHalfSweep) <= 1e-9 || Math.Abs(tanHalfSweep) <= 1e-9)
            return false;

        var radius = halfChord / Math.Abs(sinHalfSweep);
        var centerOffset = halfChord / tanHalfSweep;
        var midpoint = Midpoint(start, end);
        var leftNormal = chord.Normalize().Perpendicular();
        var center = sweepAngleRadians > 0
            ? midpoint + leftNormal * centerOffset
            : midpoint - leftNormal * centerOffset;
        var startAngle = AngleFrom(center, start);

        if (!IsValidArcGeometry(radius, sweepAngleRadians))
            return false;

        geometry = new ArcDrawingGeometry(center, radius, startAngle, sweepAngleRadians);
        return true;
    }

    public static bool IsAngleOnSweep(double startAngleRadians, double sweepAngleRadians, double targetAngleRadians)
    {
        if (sweepAngleRadians > 0)
            return NormalizePositive(targetAngleRadians - startAngleRadians) <= sweepAngleRadians + 1e-9;

        return NormalizePositive(startAngleRadians - targetAngleRadians) <= -sweepAngleRadians + 1e-9;
    }

    public static bool IsValidArcGeometry(double radius, double sweepAngleRadians)
    {
        return radius > double.Epsilon &&
               Math.Abs(sweepAngleRadians) > 1e-9 &&
               Math.Abs(sweepAngleRadians) <= TwoPi;
    }

    public static bool TryCreateCircleFromDiameterPoints(
        CadPointD first,
        CadPointD second,
        out CadPointD center,
        out double radius)
    {
        center = Midpoint(first, second);
        radius = first.DistanceTo(second) * 0.5;
        return IsValidCircleGeometry(radius);
    }

    public static bool TryCreateCircleFromThreePoints(
        CadPointD first,
        CadPointD second,
        CadPointD third,
        out CadPointD center,
        out double radius)
    {
        center = CadPointD.Origin;
        radius = 0;

        var d = 2.0 * (
            first.X * (second.Y - third.Y) +
            second.X * (third.Y - first.Y) +
            third.X * (first.Y - second.Y));
        if (Math.Abs(d) <= 1e-9)
            return false;

        var firstSquared = first.X * first.X + first.Y * first.Y;
        var secondSquared = second.X * second.X + second.Y * second.Y;
        var thirdSquared = third.X * third.X + third.Y * third.Y;
        center = new CadPointD(
            (firstSquared * (second.Y - third.Y) +
             secondSquared * (third.Y - first.Y) +
             thirdSquared * (first.Y - second.Y)) / d,
            (firstSquared * (third.X - second.X) +
             secondSquared * (first.X - third.X) +
             thirdSquared * (second.X - first.X)) / d);
        radius = center.DistanceTo(first);

        return IsValidCircleGeometry(radius) &&
               Math.Abs(center.DistanceTo(second) - radius) <= Math.Max(1e-7, radius * 1e-7) &&
               Math.Abs(center.DistanceTo(third) - radius) <= Math.Max(1e-7, radius * 1e-7);
    }

    public static bool IsValidCircleGeometry(double radius)
    {
        return radius > double.Epsilon &&
               !double.IsNaN(radius) &&
               !double.IsInfinity(radius);
    }

    public static CadPointD Midpoint(CadPointD first, CadPointD second)
    {
        return new CadPointD((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5);
    }

    public static bool TryCreateEllipseFromCenter(
        CadPointD center,
        CadPointD axisEnd,
        CadPointD otherAxisPoint,
        out EllipseDrawingGeometry geometry)
    {
        geometry = default;
        var axisVector = axisEnd - center;
        if (axisVector.Length <= double.Epsilon)
            return false;

        double radiusX;
        double radiusY;
        if (Math.Abs(axisVector.X) >= Math.Abs(axisVector.Y))
        {
            radiusX = Math.Abs(axisVector.X);
            radiusY = Math.Abs(otherAxisPoint.Y - center.Y);
        }
        else
        {
            radiusX = Math.Abs(otherAxisPoint.X - center.X);
            radiusY = Math.Abs(axisVector.Y);
        }

        if (!IsValidEllipseGeometry(radiusX, radiusY))
            return false;

        geometry = new EllipseDrawingGeometry(center, radiusX, radiusY);
        return true;
    }

    public static bool TryCreateEllipseFromAxisEnd(
        CadPointD axisStart,
        CadPointD axisEnd,
        CadPointD otherAxisPoint,
        out EllipseDrawingGeometry geometry)
    {
        geometry = default;
        var center = Midpoint(axisStart, axisEnd);
        var axisVector = axisEnd - axisStart;
        if (axisVector.Length <= double.Epsilon)
            return false;

        double radiusX;
        double radiusY;
        if (Math.Abs(axisVector.X) >= Math.Abs(axisVector.Y))
        {
            radiusX = Math.Abs(axisVector.X) * 0.5;
            radiusY = Math.Abs(otherAxisPoint.Y - center.Y);
        }
        else
        {
            radiusX = Math.Abs(otherAxisPoint.X - center.X);
            radiusY = Math.Abs(axisVector.Y) * 0.5;
        }

        if (!IsValidEllipseGeometry(radiusX, radiusY))
            return false;

        geometry = new EllipseDrawingGeometry(center, radiusX, radiusY);
        return true;
    }

    public static bool TryCreateEllipseArcFromPoints(
        CadPointD axisStart,
        CadPointD axisEnd,
        CadPointD otherAxisPoint,
        CadPointD startAnglePoint,
        CadPointD endAnglePoint,
        out EllipseArcDrawingGeometry geometry)
    {
        geometry = default;
        if (!TryCreateEllipseFromAxisEnd(axisStart, axisEnd, otherAxisPoint, out var ellipse))
            return false;

        var startAngle = EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, startAnglePoint);
        var endAngle = EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, endAnglePoint);
        var sweepAngle = ResolveSweepAngle(startAngle, endAngle, counterClockwise: true);
        if (!IsValidArcGeometry(1.0, sweepAngle))
            return false;

        geometry = new EllipseArcDrawingGeometry(
            ellipse.Center,
            ellipse.RadiusX,
            ellipse.RadiusY,
            startAngle,
            sweepAngle);
        return true;
    }

    public static double EllipseAngleFrom(CadPointD center, double radiusX, double radiusY, CadPointD point)
    {
        return Math.Atan2(
            (point.Y - center.Y) / Math.Max(radiusY, double.Epsilon),
            (point.X - center.X) / Math.Max(radiusX, double.Epsilon));
    }

    public static CadPointD GetEllipsePoint(CadPointD center, double radiusX, double radiusY, double angleRadians)
    {
        return new CadPointD(
            center.X + Math.Cos(angleRadians) * radiusX,
            center.Y + Math.Sin(angleRadians) * radiusY);
    }

    public static bool IsValidEllipseGeometry(double radiusX, double radiusY)
    {
        return radiusX > double.Epsilon &&
               radiusY > double.Epsilon &&
               !double.IsNaN(radiusX) &&
               !double.IsNaN(radiusY) &&
               !double.IsInfinity(radiusX) &&
               !double.IsInfinity(radiusY);
    }

    public static double AngleFrom(CadPointD center, CadPointD point)
    {
        return Math.Atan2(point.Y - center.Y, point.X - center.X);
    }

    public static double ResolveSweepAngle(double startAngleRadians, double endAngleRadians, bool counterClockwise)
    {
        return counterClockwise
            ? NormalizePositive(endAngleRadians - startAngleRadians)
            : -NormalizePositive(startAngleRadians - endAngleRadians);
    }

    public static double NormalizePositive(double angleRadians)
    {
        var result = angleRadians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    public static CadPointD GetArcPoint(CadPointD center, double radius, double angleRadians)
    {
        return new CadPointD(
            center.X + Math.Cos(angleRadians) * radius,
            center.Y + Math.Sin(angleRadians) * radius);
    }

    private static bool FailArcGeometry(out ArcDrawingGeometry geometry)
    {
        geometry = default;
        return false;
    }
}
