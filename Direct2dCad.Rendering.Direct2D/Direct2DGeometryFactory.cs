using System.Numerics;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DGeometryFactory
{
    private const double TwoPi = Math.PI * 2.0;
    private const double FullCircleTolerance = 1e-9;

    public RoundedRectangle CreateRoundedRectangle(CadRectD bounds, double radiusX, double radiusY)
    {
        return new RoundedRectangle(
            new System.Drawing.RectangleF(
                (float)bounds.MinX,
                (float)bounds.MinY,
                (float)bounds.Width,
                (float)bounds.Height),
            (float)ClampCornerRadius(radiusX, bounds.Width),
            (float)ClampCornerRadius(radiusY, bounds.Height));
    }

    public double ClampCornerRadius(double radius, double size)
    {
        return radius <= 0 || !double.IsFinite(radius) ? 0 : Math.Min(radius, size * 0.5);
    }

    public ID2D1PathGeometry CreatePolyline(
        ID2D1Factory factory,
        IReadOnlyList<CadPointD> points,
        bool closed)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(points[0]), closed ? FigureBegin.Filled : FigureBegin.Hollow);
        for (var index = 1; index < points.Count; index++)
            sink.AddLine(ToVector2(points[index]));
        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    public ID2D1PathGeometry CreateSpline(
        ID2D1Factory factory,
        IReadOnlyList<CadPointD> fitPoints,
        bool closed)
    {
        var geometry = factory.CreatePathGeometry();
        var segments = CadSpline.CreateBezierSegments(fitPoints, closed);
        if (segments.Count == 0)
            return geometry;

        using var sink = geometry.Open();
        sink.BeginFigure(ToVector2(segments[0].Start), closed ? FigureBegin.Filled : FigureBegin.Hollow);
        foreach (var segment in segments)
        {
            sink.AddBezier(new BezierSegment(
                ToVector2(segment.Control1),
                ToVector2(segment.Control2),
                ToVector2(segment.End)));
        }

        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    public ID2D1PathGeometry CreateArc(
        ID2D1Factory factory,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        var startPoint = GetArcPoint(center, radius, startAngleRadians);
        sink.BeginFigure(ToVector2(startPoint), FigureBegin.Hollow);
        if (IsFullCircleSweep(sweepAngleRadians))
        {
            var halfSweep = sweepAngleRadians >= 0 ? Math.PI : -Math.PI;
            sink.AddArc(CreateArcSegment(GetArcPoint(center, radius, startAngleRadians + halfSweep), radius, halfSweep));
            sink.AddArc(CreateArcSegment(startPoint, radius, halfSweep));
        }
        else
        {
            sink.AddArc(CreateArcSegment(
                GetArcPoint(center, radius, startAngleRadians + sweepAngleRadians),
                radius,
                sweepAngleRadians));
        }

        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    public ID2D1PathGeometry CreateEllipseArc(
        ID2D1Factory factory,
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        var startPoint = GetEllipsePoint(center, radiusX, radiusY, startAngleRadians);
        var endPoint = GetEllipsePoint(center, radiusX, radiusY, startAngleRadians + sweepAngleRadians);
        sink.BeginFigure(ToVector2(startPoint), FigureBegin.Hollow);
        sink.AddArc(CreateEllipseArcSegment(endPoint, radiusX, radiusY, sweepAngleRadians));
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    private static ArcSegment CreateArcSegment(CadPointD endPoint, double radius, double sweep)
    {
        return new ArcSegment(
            ToVector2(endPoint),
            new Size((float)radius, (float)radius),
            0,
            ToSweepDirection(sweep),
            Math.Abs(sweep) > Math.PI ? ArcSize.Large : ArcSize.Small);
    }

    private static ArcSegment CreateEllipseArcSegment(
        CadPointD endPoint,
        double radiusX,
        double radiusY,
        double sweep)
    {
        return new ArcSegment(
            ToVector2(endPoint),
            new Size((float)radiusX, (float)radiusY),
            0,
            ToSweepDirection(sweep),
            Math.Abs(sweep) > Math.PI ? ArcSize.Large : ArcSize.Small);
    }

    private static SweepDirection ToSweepDirection(double sweep)
    {
        return sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
    }

    private static bool IsFullCircleSweep(double sweep)
    {
        return Math.Abs(Math.Abs(sweep) - TwoPi) <= FullCircleTolerance;
    }

    private static CadPointD GetArcPoint(CadPointD center, double radius, double angle)
    {
        return new CadPointD(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }

    private static CadPointD GetEllipsePoint(
        CadPointD center,
        double radiusX,
        double radiusY,
        double angle)
    {
        return new CadPointD(center.X + Math.Cos(angle) * radiusX, center.Y + Math.Sin(angle) * radiusY);
    }

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);
}
