using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

internal static class CadEntityTransform
{
    internal static void Translate(CadEntity entity, CadVectorD delta)
    {
        switch (entity)
        {
            case CadLine line:
                line.SetGeometry(line.Start + delta, line.End + delta);
                break;
            case CadCircle circle:
                circle.SetCenter(circle.Center + delta);
                break;
            case CadEllipse ellipse:
                ellipse.SetCenter(ellipse.Center + delta);
                break;
            case CadEllipseArc ellipseArc:
                ellipseArc.SetCenter(ellipseArc.Center + delta);
                break;
            case CadRectangle rectangle:
                rectangle.SetBounds(rectangle.Bounds.Translate(delta));
                break;
            case CadArc arc:
                arc.SetCenter(arc.Center + delta);
                break;
            case CadPolyline polyline:
                polyline.ReplacePoints(polyline.Points.Select(x => x + delta));
                break;
            case CadSpline spline:
                spline.ReplaceFitPoints(spline.FitPoints.Select(x => x + delta));
                break;
            case CadCompositePath path:
                TransformCompositePath(path, point => point + delta);
                break;
            case CadText text:
                text.SetPosition(text.Position + delta);
                break;
            case CadShapeText shapeText:
                shapeText.SetPosition(shapeText.Position + delta);
                break;
            case CadImage image:
                image.SetBounds(image.FrameBounds.Translate(delta));
                break;
            case CadOleObject oleObject:
                oleObject.SetBounds(oleObject.Bounds.Translate(delta));
                break;
            case CadBlockReference blockReference:
                blockReference.SetPosition(blockReference.Position + delta);
                break;
            default:
                throw new NotSupportedException($"Entity type is not movable: {entity.GetType().Name}");
        }
    }

    internal static void ValidateRotation(CadEntity entity, double angleRadians)
    {
        if (!double.IsFinite(angleRadians))
            throw new ArgumentOutOfRangeException(nameof(angleRadians));
        if (entity is CadEllipse or CadEllipseArc or CadRectangle && !TryGetQuarterTurns(angleRadians, out _))
            throw new NotSupportedException($"{entity.GetType().Name} only supports rotation in 90 degree increments.");
        if (entity is CadOleObject)
            throw new NotSupportedException($"Entity type is not rotatable by this command: {entity.GetType().Name}");
    }

    internal static void Rotate(CadEntity entity, CadPointD pivot, double angleRadians)
    {
        ValidateRotation(entity, angleRadians);
        switch (entity)
        {
            case CadLine line:
                line.SetGeometry(RotatePoint(line.Start, pivot, angleRadians), RotatePoint(line.End, pivot, angleRadians));
                break;
            case CadCircle circle:
                circle.SetCenter(RotatePoint(circle.Center, pivot, angleRadians));
                break;
            case CadEllipse ellipse:
                RotateEllipse(ellipse, pivot, angleRadians);
                break;
            case CadEllipseArc ellipseArc:
                RotateEllipseArc(ellipseArc, pivot, angleRadians);
                break;
            case CadRectangle rectangle:
                RotateRectangle(rectangle, pivot, angleRadians);
                break;
            case CadArc arc:
                arc.SetGeometry(
                    RotatePoint(arc.Center, pivot, angleRadians),
                    arc.Radius,
                    arc.StartAngleRadians + angleRadians,
                    arc.SweepAngleRadians);
                break;
            case CadPolyline polyline:
                polyline.ReplacePoints(polyline.Points.Select(point => RotatePoint(point, pivot, angleRadians)));
                break;
            case CadSpline spline:
                spline.ReplaceFitPoints(spline.FitPoints.Select(point => RotatePoint(point, pivot, angleRadians)));
                break;
            case CadCompositePath path:
                TransformCompositePath(path, point => RotatePoint(point, pivot, angleRadians));
                break;
            case CadText text:
                text.SetPosition(RotatePoint(text.Position, pivot, angleRadians));
                text.SetRotation(text.RotationRadians + angleRadians);
                break;
            case CadShapeText shapeText:
                shapeText.SetPosition(RotatePoint(shapeText.Position, pivot, angleRadians));
                shapeText.SetRotation(shapeText.RotationRadians + angleRadians);
                break;
            case CadImage image:
                MoveBoundsCenter(image, RotatePoint(image.FrameBounds.Center, pivot, angleRadians));
                image.SetRotation(image.RotationRadians + angleRadians);
                break;
            case CadBlockReference blockReference:
                blockReference.SetPosition(RotatePoint(blockReference.Position, pivot, angleRadians));
                blockReference.SetRotation(blockReference.RotationRadians + angleRadians);
                break;
            default:
                throw new NotSupportedException($"Entity type is not rotatable: {entity.GetType().Name}");
        }
    }

    internal static void ValidateUniformScale(CadEntity entity, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Scale factor must be greater than zero.");
    }

    internal static void UniformScale(CadEntity entity, CadPointD pivot, double factor)
    {
        ValidateUniformScale(entity, factor);
        switch (entity)
        {
            case CadLine line:
                line.SetGeometry(ScalePoint(line.Start, pivot, factor), ScalePoint(line.End, pivot, factor));
                break;
            case CadCircle circle:
                circle.SetGeometry(ScalePoint(circle.Center, pivot, factor), circle.Radius * factor);
                break;
            case CadEllipse ellipse:
                ellipse.SetGeometry(ScalePoint(ellipse.Center, pivot, factor), ellipse.RadiusX * factor, ellipse.RadiusY * factor);
                break;
            case CadEllipseArc ellipseArc:
                ellipseArc.SetGeometry(
                    ScalePoint(ellipseArc.Center, pivot, factor),
                    ellipseArc.RadiusX * factor,
                    ellipseArc.RadiusY * factor,
                    ellipseArc.StartAngleRadians,
                    ellipseArc.SweepAngleRadians);
                break;
            case CadRectangle rectangle:
                var radiusX = rectangle.CornerRadiusX * factor;
                var radiusY = rectangle.CornerRadiusY * factor;
                rectangle.SetBounds(TransformRect(rectangle.Bounds, point => ScalePoint(point, pivot, factor)));
                rectangle.SetCornerRadius(radiusX, radiusY);
                break;
            case CadArc arc:
                arc.SetGeometry(ScalePoint(arc.Center, pivot, factor), arc.Radius * factor, arc.StartAngleRadians, arc.SweepAngleRadians);
                break;
            case CadPolyline polyline:
                polyline.ReplacePoints(polyline.Points.Select(point => ScalePoint(point, pivot, factor)));
                break;
            case CadSpline spline:
                spline.ReplaceFitPoints(spline.FitPoints.Select(point => ScalePoint(point, pivot, factor)));
                break;
            case CadCompositePath path:
                TransformCompositePath(path, point => ScalePoint(point, pivot, factor));
                break;
            case CadText text:
                text.SetPosition(ScalePoint(text.Position, pivot, factor));
                text.SetHeight(text.Height * factor);
                break;
            case CadShapeText shapeText:
                shapeText.SetPosition(ScalePoint(shapeText.Position, pivot, factor));
                shapeText.SetHeight(shapeText.Height * factor);
                break;
            case CadImage image:
                image.SetBounds(TransformRect(image.FrameBounds, point => ScalePoint(point, pivot, factor)));
                break;
            case CadOleObject oleObject:
                oleObject.SetBounds(TransformRect(oleObject.Bounds, point => ScalePoint(point, pivot, factor)));
                break;
            case CadBlockReference blockReference:
                blockReference.SetPosition(ScalePoint(blockReference.Position, pivot, factor));
                blockReference.SetScale(blockReference.ScaleX * factor, blockReference.ScaleY * factor);
                break;
            default:
                throw new NotSupportedException($"Entity type is not scalable: {entity.GetType().Name}");
        }
    }

    internal static void ValidateMirror(CadEntity entity, double axisAngleRadians)
    {
        if (!double.IsFinite(axisAngleRadians))
            throw new ArgumentOutOfRangeException(nameof(axisAngleRadians));
        if (entity is CadEllipse or CadEllipseArc or CadRectangle && !TryGetEighthTurns(axisAngleRadians, out _))
            throw new NotSupportedException($"{entity.GetType().Name} only supports mirror axes in 45 degree increments.");
        if (entity is CadOleObject && !TryGetQuarterTurns(axisAngleRadians, out _))
            throw new NotSupportedException("CadOleObject only supports horizontal or vertical mirror axes.");
    }

    internal static void Mirror(CadEntity entity, CadPointD axisPoint, double axisAngleRadians)
    {
        ValidateMirror(entity, axisAngleRadians);
        CadPointD Transform(CadPointD point) => MirrorPoint(point, axisPoint, axisAngleRadians);
        switch (entity)
        {
            case CadLine line:
                line.SetGeometry(Transform(line.Start), Transform(line.End));
                break;
            case CadCircle circle:
                circle.SetCenter(Transform(circle.Center));
                break;
            case CadEllipse ellipse:
                var swapRadii = TryGetEighthTurns(axisAngleRadians, out var ellipseTurns) && Math.Abs(ellipseTurns) % 2 == 1;
                ellipse.SetGeometry(
                    Transform(ellipse.Center),
                    swapRadii ? ellipse.RadiusY : ellipse.RadiusX,
                    swapRadii ? ellipse.RadiusX : ellipse.RadiusY);
                break;
            case CadEllipseArc ellipseArc:
                _ = TryGetEighthTurns(axisAngleRadians, out var ellipseArcTurns);
                var swapEllipseArcRadii = Math.Abs(ellipseArcTurns) % 2 == 1;
                ellipseArc.SetGeometry(
                    Transform(ellipseArc.Center),
                    swapEllipseArcRadii ? ellipseArc.RadiusY : ellipseArc.RadiusX,
                    swapEllipseArcRadii ? ellipseArc.RadiusX : ellipseArc.RadiusY,
                    2 * axisAngleRadians - ellipseArc.StartAngleRadians,
                    -ellipseArc.SweepAngleRadians);
                break;
            case CadRectangle rectangle:
                var rectangleRadiusX = rectangle.CornerRadiusX;
                var rectangleRadiusY = rectangle.CornerRadiusY;
                rectangle.SetBounds(TransformRect(rectangle.Bounds, Transform));
                _ = TryGetEighthTurns(axisAngleRadians, out var rectangleTurns);
                var swapRectangleRadii = Math.Abs(rectangleTurns) % 2 == 1;
                rectangle.SetCornerRadius(
                    swapRectangleRadii ? rectangleRadiusY : rectangleRadiusX,
                    swapRectangleRadii ? rectangleRadiusX : rectangleRadiusY);
                break;
            case CadArc arc:
                arc.SetGeometry(
                    Transform(arc.Center),
                    arc.Radius,
                    2 * axisAngleRadians - arc.StartAngleRadians,
                    -arc.SweepAngleRadians);
                break;
            case CadPolyline polyline:
                polyline.ReplacePoints(polyline.Points.Select(Transform));
                break;
            case CadSpline spline:
                spline.ReplaceFitPoints(spline.FitPoints.Select(Transform));
                break;
            case CadCompositePath path:
                TransformCompositePath(path, Transform, reverseArcDirection: true);
                break;
            case CadText text:
                text.SetPosition(Transform(text.Position));
                text.SetRotation(2 * axisAngleRadians - text.RotationRadians);
                break;
            case CadShapeText shapeText:
                shapeText.SetPosition(Transform(shapeText.Position));
                shapeText.SetRotation(2 * axisAngleRadians - shapeText.RotationRadians);
                break;
            case CadImage image:
                MoveBoundsCenter(image, Transform(image.FrameBounds.Center));
                image.SetRotation(2 * axisAngleRadians - image.RotationRadians);
                break;
            case CadOleObject oleObject:
                oleObject.SetBounds(TransformRect(oleObject.Bounds, Transform));
                break;
            case CadBlockReference blockReference:
                blockReference.SetPosition(Transform(blockReference.Position));
                blockReference.SetRotation(2 * axisAngleRadians - blockReference.RotationRadians);
                blockReference.SetScale(blockReference.ScaleX, -blockReference.ScaleY);
                break;
            default:
                throw new NotSupportedException($"Entity type is not mirrorable: {entity.GetType().Name}");
        }
    }

    private static void TransformCompositePath(
        CadCompositePath path,
        Func<CadPointD, CadPointD> transform,
        bool reverseArcDirection = false)
    {
        var segments = path.Segments.Select<CadCompositePathSegment, CadCompositePathSegment>(segment => segment switch
        {
            CadCompositeLineSegment line => new CadCompositeLineSegment(transform(line.End)),
            CadCompositeArcSegment arc => new CadCompositeArcSegment(
                transform(arc.Center),
                reverseArcDirection ? -arc.SweepAngleRadians : arc.SweepAngleRadians),
            CadCompositeSplineSegment spline => new CadCompositeSplineSegment(spline.FitPoints.Select(transform)),
            CadCompositeBezierSegment bezier => new CadCompositeBezierSegment(
                transform(bezier.Control1),
                transform(bezier.Control2),
                transform(bezier.End)),
            _ => throw new NotSupportedException($"Unsupported composite path segment: {segment.GetType().Name}")
        }).ToArray();
        path.ReplaceGeometry(transform(path.StartPoint), segments, path.Closed);
    }

    private static CadPointD RotatePoint(CadPointD point, CadPointD pivot, double angleRadians)
    {
        var dx = point.X - pivot.X;
        var dy = point.Y - pivot.Y;
        var cosine = Math.Cos(angleRadians);
        var sine = Math.Sin(angleRadians);
        return new CadPointD(pivot.X + dx * cosine - dy * sine, pivot.Y + dx * sine + dy * cosine);
    }

    private static CadPointD ScalePoint(CadPointD point, CadPointD pivot, double factor) => new(
        pivot.X + (point.X - pivot.X) * factor,
        pivot.Y + (point.Y - pivot.Y) * factor);

    private static CadPointD MirrorPoint(CadPointD point, CadPointD axisPoint, double axisAngleRadians)
    {
        var cosine = Math.Cos(axisAngleRadians);
        var sine = Math.Sin(axisAngleRadians);
        var dx = point.X - axisPoint.X;
        var dy = point.Y - axisPoint.Y;
        var along = dx * cosine + dy * sine;
        var normal = -dx * sine + dy * cosine;
        return new CadPointD(
            axisPoint.X + along * cosine + normal * sine,
            axisPoint.Y + along * sine - normal * cosine);
    }

    private static CadRectD TransformRect(CadRectD bounds, Func<CadPointD, CadPointD> transform)
    {
        var result = CadRectD.Empty;
        result = result.ExpandToInclude(transform(new CadPointD(bounds.MinX, bounds.MinY)));
        result = result.ExpandToInclude(transform(new CadPointD(bounds.MaxX, bounds.MinY)));
        result = result.ExpandToInclude(transform(new CadPointD(bounds.MaxX, bounds.MaxY)));
        return result.ExpandToInclude(transform(new CadPointD(bounds.MinX, bounds.MaxY)));
    }

    private static void RotateEllipse(CadEllipse ellipse, CadPointD pivot, double angleRadians)
    {
        _ = TryGetQuarterTurns(angleRadians, out var turns);
        var swapRadii = Math.Abs(turns) % 2 == 1;
        ellipse.SetGeometry(
            RotatePoint(ellipse.Center, pivot, angleRadians),
            swapRadii ? ellipse.RadiusY : ellipse.RadiusX,
            swapRadii ? ellipse.RadiusX : ellipse.RadiusY);
    }

    private static void RotateEllipseArc(CadEllipseArc ellipseArc, CadPointD pivot, double angleRadians)
    {
        _ = TryGetQuarterTurns(angleRadians, out var turns);
        var swapRadii = Math.Abs(turns) % 2 == 1;
        ellipseArc.SetGeometry(
            RotatePoint(ellipseArc.Center, pivot, angleRadians),
            swapRadii ? ellipseArc.RadiusY : ellipseArc.RadiusX,
            swapRadii ? ellipseArc.RadiusX : ellipseArc.RadiusY,
            ellipseArc.StartAngleRadians + angleRadians,
            ellipseArc.SweepAngleRadians);
    }

    private static void RotateRectangle(CadRectangle rectangle, CadPointD pivot, double angleRadians)
    {
        var radiusX = rectangle.CornerRadiusX;
        var radiusY = rectangle.CornerRadiusY;
        rectangle.SetBounds(TransformRect(rectangle.Bounds, point => RotatePoint(point, pivot, angleRadians)));
        _ = TryGetQuarterTurns(angleRadians, out var turns);
        var swapRadii = Math.Abs(turns) % 2 == 1;
        rectangle.SetCornerRadius(swapRadii ? radiusY : radiusX, swapRadii ? radiusX : radiusY);
    }

    private static void MoveBoundsCenter(CadImage image, CadPointD center)
    {
        var bounds = image.FrameBounds;
        image.SetBounds(CadRectD.FromLTRB(
            center.X - bounds.Width * 0.5,
            center.Y - bounds.Height * 0.5,
            center.X + bounds.Width * 0.5,
            center.Y + bounds.Height * 0.5));
    }

    private static bool TryGetQuarterTurns(double angleRadians, out int turns) =>
        TryGetDiscreteTurns(angleRadians, Math.PI * 0.5, out turns);

    private static bool TryGetEighthTurns(double angleRadians, out int turns) =>
        TryGetDiscreteTurns(angleRadians, Math.PI * 0.25, out turns);

    private static bool TryGetDiscreteTurns(double angleRadians, double step, out int turns)
    {
        turns = (int)Math.Round(angleRadians / step);
        return Math.Abs(angleRadians - turns * step) <= 1e-8;
    }
}
