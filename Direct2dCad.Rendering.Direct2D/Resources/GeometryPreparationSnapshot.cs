using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Resources;

// Builders capture values and private arrays, never a live document or entity.
internal readonly record struct GeometryPreparationSnapshot(
    EntityId EntityId, Func<ID2D1Factory, (ID2D1Geometry? Geometry, int Complexity)>? Build = null)
{
    private static readonly Direct2DGeometryFactory GeometryFactory = new();

    public Direct2DPreparedGeometry Prepare(ID2D1Factory factory)
    {
        var (geometry, complexity) = Build?.Invoke(factory) ?? (null, 0);
        return new Direct2DPreparedGeometry(EntityId, geometry, complexity);
    }

    public static GeometryPreparationSnapshot Capture(CadDocument document, CadEntity entity)
    {
        var id = entity.Id;
        var fillId = entity switch
        {
            CadCircle item => item.FillStyleId,
            CadEllipse item => item.FillStyleId,
            CadRectangle item => item.FillStyleId,
            _ => null
        };
        var primitiveFill = fillId is { } fill && document.TryGetStyle(fill, out var style) && style is CadHatchFillStyle;
        if (entity.IsErased || !entity.IsVisible)
            return new(id);
        switch (entity)
        {
            case CadCircle circle when primitiveFill:
                var circular = new Ellipse(Point(circle.Center), (float)circle.Radius, (float)circle.Radius);
                return new(id, factory => (factory.CreateEllipseGeometry(circular), 0));
            case CadEllipse ellipse when primitiveFill:
                var elliptical = new Ellipse(Point(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY);
                return new(id, factory => (factory.CreateEllipseGeometry(elliptical), 0));
            case CadRectangle rectangle when primitiveFill:
                var rounded = GeometryFactory.CreateRoundedRectangle(rectangle.Bounds, rectangle.CornerRadiusX, rectangle.CornerRadiusY);
                return new(id, factory => (rounded.RadiusX > 0 && rounded.RadiusY > 0
                    ? factory.CreateRoundedRectangleGeometry(rounded)
                    : factory.CreateRectangleGeometry(rounded.Rect), 0));
            case CadArc arc when !arc.IsFullCircle:
                var arcData = (arc.Center, arc.Radius, arc.StartAngleRadians, arc.SweepAngleRadians);
                return new(id, factory => (GeometryFactory.CreateArc(factory, arcData.Center, arcData.Radius,
                    arcData.StartAngleRadians, arcData.SweepAngleRadians), 0));
            case CadEllipseArc arc:
                var ellipseArc = (arc.Center, arc.RadiusX, arc.RadiusY, arc.StartAngleRadians, arc.SweepAngleRadians);
                return new(id, factory => (GeometryFactory.CreateEllipseArc(factory, ellipseArc.Center,
                    ellipseArc.RadiusX, ellipseArc.RadiusY, ellipseArc.StartAngleRadians, ellipseArc.SweepAngleRadians), 0));
            case CadPolyline polyline:
                var points = polyline.Points.ToArray();
                var polylineClosed = polyline.Closed;
                return new(id, factory => (GeometryFactory.CreatePolyline(factory, points, polylineClosed), points.Length));
            case CadSpline spline:
                var fitPoints = spline.FitPoints.ToArray();
                var splineClosed = spline.Closed;
                return new(id, factory => (GeometryFactory.CreateSpline(factory, fitPoints, splineClosed),
                    splineClosed ? fitPoints.Length : fitPoints.Length - 1));
            case CadCompositePath path:
                var segments = path.Segments.Select(segment => segment is CadCompositeSplineSegment splineSegment
                    ? new CadCompositeSplineSegment(splineSegment.FitPoints) : segment).ToArray();
                var start = path.StartPoint;
                var pathClosed = path.Closed;
                return new(id, factory => (GeometryFactory.CreateCompositePath(factory, start, segments, pathClosed), segments.Length));
            case CadShapeText text:
                var textData = (text.Text, text.Position, text.Height, text.WidthFactor, text.CharacterSpacingFactor,
                    text.ObliqueAngleRadians, text.RotationRadians, text.ShapeFontId);
                return new(id, factory =>
                {
                    var strokes = CadStrokeFont.CreateSegments(textData.Text,
                        textData.Position, textData.Height, textData.WidthFactor, textData.CharacterSpacingFactor,
                        textData.ObliqueAngleRadians, textData.RotationRadians, textData.ShapeFontId);
                    return (GeometryFactory.CreateStrokeText(factory, strokes), strokes.Count);
                });
            default:
                return new(id);
        }
    }

    private static Vector2 Point(CadPointD point) => new((float)point.X, (float)point.Y);
}
