using System.Windows;
using System.Windows.Media;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.wpf.Services.Printing.Vector;

internal static class CadVectorPrintGeometryFactory
{
    public static Geometry? Create(CadEntity entity) => entity switch
    {
        CadLine line => new LineGeometry(ToPoint(line.Start), ToPoint(line.End)),
        CadCircle circle => new EllipseGeometry(ToPoint(circle.Center), circle.Radius, circle.Radius),
        CadArc { IsFullCircle: true } arc =>
            new EllipseGeometry(ToPoint(arc.Center), arc.Radius, arc.Radius),
        CadArc arc => CreateArc(
            arc.Center,
            arc.Radius,
            arc.Radius,
            arc.StartAngleRadians,
            arc.SweepAngleRadians),
        CadEllipse ellipse =>
            new EllipseGeometry(ToPoint(ellipse.Center), ellipse.RadiusX, ellipse.RadiusY),
        CadEllipseArc ellipseArc => CreateArc(
            ellipseArc.Center,
            ellipseArc.RadiusX,
            ellipseArc.RadiusY,
            ellipseArc.StartAngleRadians,
            ellipseArc.SweepAngleRadians),
        CadRectangle rectangle => CreateRectangle(rectangle),
        CadPolyline polyline => CreatePolyline(polyline.Points, polyline.Closed),
        CadSpline spline => CreateSpline(spline.GetBezierSegments(), spline.Closed),
        CadCompositePath path => CreateCompositePath(path),
        CadShapeText shapeText => CreateShapeText(shapeText),
        _ => null
    };

    public static Geometry Transform(Geometry geometry, CadMatrixD transform)
    {
        var clone = geometry.CloneCurrentValue();
        clone.Transform = new MatrixTransform(ToMatrix(transform));
        return clone;
    }

    public static Matrix ToMatrix(CadMatrixD value) => new(
        value.M11,
        value.M12,
        value.M21,
        value.M22,
        value.OffsetX,
        value.OffsetY);

    private static Geometry CreateRectangle(CadRectangle rectangle)
    {
        var bounds = ToRect(rectangle.Bounds);
        return rectangle.HasRoundedCorners
            ? new RectangleGeometry(
                bounds,
                Math.Min(rectangle.CornerRadiusX, rectangle.Bounds.Width * 0.5),
                Math.Min(rectangle.CornerRadiusY, rectangle.Bounds.Height * 0.5))
            : new RectangleGeometry(bounds);
    }

    private static Geometry CreatePolyline(IReadOnlyList<CadPointD> points, bool closed)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var context = geometry.Open();
        context.BeginFigure(ToPoint(points[0]), closed, closed);
        for (var index = 1; index < points.Count; index++)
            context.LineTo(ToPoint(points[index]), true, false);
        return geometry;
    }

    private static Geometry CreateSpline(
        IReadOnlyList<CadBezierSegmentD> segments,
        bool closed)
    {
        if (segments.Count == 0)
            return Geometry.Empty;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var context = geometry.Open();
        context.BeginFigure(ToPoint(segments[0].Start), closed, closed);
        foreach (var segment in segments)
        {
            context.BezierTo(
                ToPoint(segment.Control1),
                ToPoint(segment.Control2),
                ToPoint(segment.End),
                true,
                false);
        }
        return geometry;
    }

    private static Geometry CreateCompositePath(CadCompositePath path)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var context = geometry.Open();
        var current = path.StartPoint;
        context.BeginFigure(ToPoint(current), path.Closed, path.Closed);
        foreach (var segment in path.Segments)
        {
            switch (segment)
            {
                case CadCompositeLineSegment line:
                    context.LineTo(ToPoint(line.End), true, false);
                    current = line.End;
                    break;
                case CadCompositeArcSegment arc:
                    AppendArc(
                        context,
                        arc.Center,
                        current.DistanceTo(arc.Center),
                        current.DistanceTo(arc.Center),
                        Math.Atan2(current.Y - arc.Center.Y, current.X - arc.Center.X),
                        arc.SweepAngleRadians);
                    current = CadCompositePath.GetEndPoint(current, arc);
                    break;
                case CadCompositeSplineSegment spline:
                    var points = new CadPointD[spline.FitPoints.Count + 1];
                    points[0] = current;
                    for (var index = 0; index < spline.FitPoints.Count; index++)
                        points[index + 1] = spline.FitPoints[index];
                    foreach (var bezier in CadSpline.CreateBezierSegments(points))
                    {
                        context.BezierTo(
                            ToPoint(bezier.Control1),
                            ToPoint(bezier.Control2),
                            ToPoint(bezier.End),
                            true,
                            false);
                    }
                    current = spline.FitPoints[^1];
                    break;
            }
        }
        return geometry;
    }

    private static Geometry CreateShapeText(CadShapeText text)
    {
        var group = new GeometryGroup();
        foreach (var segment in text.CreateStrokeSegments())
            group.Children.Add(new LineGeometry(ToPoint(segment.Start), ToPoint(segment.End)));
        return group;
    }

    private static Geometry CreateArc(
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngle,
        double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(
            ToPoint(GetEllipsePoint(center, radiusX, radiusY, startAngle)),
            false,
            false);
        AppendArc(context, center, radiusX, radiusY, startAngle, sweepAngle);
        return geometry;
    }

    private static void AppendArc(
        StreamGeometryContext context,
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngle,
        double sweepAngle)
    {
        var segmentCount = Math.Max(
            1,
            (int)Math.Ceiling(Math.Abs(sweepAngle) / Math.PI));
        var segmentSweep = sweepAngle / segmentCount;
        var angle = startAngle;
        for (var index = 0; index < segmentCount; index++)
        {
            var nextAngle = angle + segmentSweep;
            var end = GetEllipsePoint(center, radiusX, radiusY, nextAngle);
            context.ArcTo(
                ToPoint(end),
                new Size(radiusX, radiusY),
                rotationAngle: 0,
                isLargeArc: Math.Abs(segmentSweep) > Math.PI,
                sweepDirection: segmentSweep > 0
                    ? SweepDirection.Clockwise
                    : SweepDirection.Counterclockwise,
                isStroked: true,
                isSmoothJoin: false);
            angle = nextAngle;
        }
    }

    private static CadPointD GetEllipsePoint(
        CadPointD center,
        double radiusX,
        double radiusY,
        double angle) => new(
        center.X + Math.Cos(angle) * radiusX,
        center.Y + Math.Sin(angle) * radiusY);

    public static Point ToPoint(CadPointD point) => new(point.X, point.Y);

    public static Rect ToRect(CadRectD bounds) => new(
        bounds.MinX,
        bounds.MinY,
        bounds.Width,
        bounds.Height);
}
