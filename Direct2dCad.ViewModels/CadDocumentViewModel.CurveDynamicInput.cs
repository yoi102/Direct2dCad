using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Geometry;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels;

public readonly record struct CadDynamicInputMeasurement(string Key, CadPointD Start, CadPointD End, int StackIndex = 0);

public partial class CadDocumentViewModel
{
    private bool IsEllipseDrawing => CadCanvasToolMode is CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd or CadCanvasToolMode.EllipseArc;
    private bool IsCenterArcDrawing => CadCanvasToolMode is CadCanvasToolMode.ArcStartCenterEnd or CadCanvasToolMode.ArcStartCenterAngle or
        CadCanvasToolMode.ArcStartCenterLength or CadCanvasToolMode.ArcCenterStartEnd or CadCanvasToolMode.ArcCenterStartAngle or CadCanvasToolMode.ArcCenterStartLength;
    private bool IsCenterFirstArcDrawing => CadCanvasToolMode is CadCanvasToolMode.ArcCenterStartEnd or CadCanvasToolMode.ArcCenterStartAngle or CadCanvasToolMode.ArcCenterStartLength;

    private string[] GetCurveDynamicInputKeys()
    {
        if (IsEllipseDrawing && _drawingState.PendingEllipsePoints.Count >= 2)
            return _drawingState.PendingEllipsePoints.Count switch
            {
                2 => ["AxisX", "AxisY"], 3 => ["AxisX", "AxisY", "StartAngle"], _ => ["AxisX", "AxisY", "Angle"]
            };
        if (CadCanvasToolMode == CadCanvasToolMode.CircleThreePoint && _drawingState.PendingCircleSecondPoint is not null)
            return ["Radius"];
        if (CadCanvasToolMode == CadCanvasToolMode.ArcContinue && ResolveContinueArcBase().HasValue)
            return ["Radius", "Angle"];
        if (_drawingState.PendingWorldPoint is null) return [];
        if (_drawingState.PendingArcStartPoint is null) return IsCenterArcDrawing ? ["Radius"] : [];
        return CadCanvasToolMode switch
        {
            CadCanvasToolMode.ArcThreePoint => ["Radius", "Angle"],
            CadCanvasToolMode.ArcStartCenterLength or CadCanvasToolMode.ArcCenterStartLength => ["Length"],
            CadCanvasToolMode.ArcStartEndRadius => ["Radius"],
            CadCanvasToolMode.ArcStartEndDirection => ["Direction"],
            CadCanvasToolMode.ArcStartCenterEnd or CadCanvasToolMode.ArcStartCenterAngle or CadCanvasToolMode.ArcStartEndAngle or
                CadCanvasToolMode.ArcCenterStartEnd or CadCanvasToolMode.ArcCenterStartAngle => ["Angle"],
            _ => []
        };
    }

    private double[] GetCurveDynamicInputValues(CadDrawingSessionState state, CadPointD pointer)
    {
        double Display(double value) => CadUnitConversion.FromMillimeters(value, DocumentUnit);
        if (IsEllipseDrawing)
        {
            var ellipse = GetDynamicEllipse(state, pointer);
            double[] axes = [Display(ellipse.RadiusX), Display(ellipse.RadiusY)];
            if (state.PendingEllipsePoints.Count == 2) return axes;
            var angle = EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, pointer);
            if (state.PendingEllipsePoints.Count == 4)
                angle = ResolveSweepAngle(EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, state.PendingEllipsePoints[3]), angle, true);
            return [.. axes, NormalizePositive(angle) * 180 / Math.PI];
        }
        if (CadCanvasToolMode == CadCanvasToolMode.CircleThreePoint)
        {
            var first = state.PendingWorldPoint!.Value;
            var second = state.PendingCircleSecondPoint!.Value;
            return [Display(TryCreateCircleFromThreePoints(first, second, pointer, out _, out var radius) ? radius : first.DistanceTo(second) * .5)];
        }
        if (IsCenterArcDrawing && state.PendingArcStartPoint is null)
            return [Display(state.PendingWorldPoint!.Value.DistanceTo(pointer))];
        var valid = TryGetDynamicArc(state, pointer, out var arc);
        var sweep = valid ? Math.Abs(arc.SweepAngleRadians) * 180 / Math.PI : 90;
        var fallbackRadius = state.PendingWorldPoint is { } a && state.PendingArcStartPoint is { } b ? a.DistanceTo(b) * .5 : 1;
        return CadCanvasToolMode switch
        {
            CadCanvasToolMode.ArcThreePoint or CadCanvasToolMode.ArcContinue => [Display(valid ? arc.Radius : fallbackRadius), sweep],
            CadCanvasToolMode.ArcStartEndRadius => [Display(valid ? arc.Radius : fallbackRadius)],
            CadCanvasToolMode.ArcStartEndDirection => [NormalizePositive(AngleFrom(state.PendingWorldPoint!.Value, pointer)) * 180 / Math.PI],
            CadCanvasToolMode.ArcStartCenterLength or CadCanvasToolMode.ArcCenterStartLength =>
                [valid ? Display(GetArcPoint(arc.Center, arc.Radius, arc.StartAngleRadians).DistanceTo(GetArcPoint(arc.Center, arc.Radius, arc.StartAngleRadians + arc.SweepAngleRadians))) : 0],
            _ => [sweep]
        };
    }

    private EllipseDrawingGeometry GetDynamicEllipse(CadDrawingSessionState state, CadPointD pointer)
    {
        var points = state.PendingEllipsePoints;
        var centered = CadCanvasToolMode == CadCanvasToolMode.EllipseCenter;
        var center = centered ? points[0] : Midpoint(points[0], points[1]);
        var axis = points[1] - points[0];
        var other = points.Count == 2 ? pointer : points[2];
        var factor = centered ? 1 : .5;
        return Math.Abs(axis.X) >= Math.Abs(axis.Y)
            ? new(center, Math.Abs(axis.X) * factor, Math.Abs(other.Y - center.Y))
            : new(center, Math.Abs(other.X - center.X), Math.Abs(axis.Y) * factor);
    }

    private bool TryGetDynamicArc(CadDrawingSessionState state, CadPointD pointer, out ArcDrawingGeometry geometry)
    {
        if (CadCanvasToolMode == CadCanvasToolMode.ArcContinue)
        {
            var basis = ResolveContinueArcBase();
            geometry = default;
            return basis.HasValue && TryCreateArcFromStartEndTangent(basis.Start, pointer, basis.Tangent, out geometry);
        }
        geometry = default;
        return state.PendingWorldPoint is { } first && state.PendingArcStartPoint is { } second &&
            TryCreateArcFromMode(CadCanvasToolMode, first, second, pointer, out geometry);
    }

    private bool IsDynamicFieldLocked(string key) => DynamicInputFields.Any(f => f.Key == key && f.IsLocked);
    private static CadVectorD InputDirection(CadPointD start, CadPointD end) => start.DistanceTo(end) > 1e-9 ? (end - start).Normalize() : CadVectorD.UnitX;
    private static bool IsInputSweep(double degrees) => degrees > 1e-7 && degrees < 360 - 1e-7;

    private bool TryResolveCurveDynamicInput(double[] values, out CadPointD point, out CadDrawingSessionState? resolvedState)
    {
        point = _dynamicInputPointer;
        resolvedState = null;
        double World(double value) => CadUnitConversion.ToMillimeters(value, DocumentUnit);
        if (IsEllipseDrawing) return TryResolveEllipseDynamicInput(values, out point, out resolvedState);
        if (CadCanvasToolMode == CadCanvasToolMode.ArcContinue)
        {
            var basis = ResolveContinueArcBase();
            if (!basis.HasValue || values[0] <= 0 || !IsInputSweep(values[1])) return false;
            TryGetDynamicArc(_drawingState, point, out var raw);
            var sign = raw.SweepAngleRadians < 0 ? -1 : 1;
            var center = basis.Start + basis.Tangent.Normalize().Perpendicular() * (sign * World(values[0]));
            point = GetArcPoint(center, World(values[0]), AngleFrom(center, basis.Start) + sign * values[1] * Math.PI / 180);
            return TryCreateArcFromStartEndTangent(basis.Start, point, basis.Tangent, out _);
        }
        var first = _drawingState.PendingWorldPoint!.Value;
        if (IsCenterArcDrawing && _drawingState.PendingArcStartPoint is null)
        {
            if (values[0] <= 0) return false;
            point = first + InputDirection(first, point) * World(values[0]);
            return true;
        }
        if (CadCanvasToolMode is CadCanvasToolMode.CircleThreePoint or CadCanvasToolMode.ArcThreePoint)
        {
            var second = CadCanvasToolMode == CadCanvasToolMode.CircleThreePoint ? _drawingState.PendingCircleSecondPoint!.Value : _drawingState.PendingArcStartPoint!.Value;
            var hasRaw = TryCreateCircleFromThreePoints(first, second, point, out var rawCenter, out _);
            if (!TryInputCircleCenter(first, second, World(values[0]), hasRaw ? rawCenter : point, out var center)) return false;
            if (CadCanvasToolMode == CadCanvasToolMode.ArcThreePoint && IsDynamicFieldLocked("Angle"))
            {
                if (!IsInputSweep(values[1])) return false;
                TryGetDynamicArc(_drawingState, point, out var rawArc);
                var sign = rawArc.SweepAngleRadians < 0 ? -1 : 1;
                point = GetArcPoint(center, World(values[0]), AngleFrom(center, first) + sign * values[1] * Math.PI / 180);
                return TryCreateArcFromThreePoints(first, second, point, out var constrained) &&
                    Math.Abs(Math.Abs(constrained.SweepAngleRadians) - values[1] * Math.PI / 180) < 1e-7;
            }
            point = center + InputDirection(center, point) * World(values[0]);
            if (point.NearEquals(first) || point.NearEquals(second))
                point = center + InputDirection(first, second).Perpendicular() * World(values[0]);
            return CadCanvasToolMode == CadCanvasToolMode.CircleThreePoint
                ? TryCreateCircleFromThreePoints(first, second, point, out _, out _)
                : TryCreateArcFromThreePoints(first, second, point, out _);
        }
        var next = _drawingState.PendingArcStartPoint!.Value;
        if (IsCenterArcDrawing)
        {
            var center = IsCenterFirstArcDrawing ? first : next;
            var start = IsCenterFirstArcDrawing ? next : first;
            if (CadCanvasToolMode is CadCanvasToolMode.ArcStartCenterLength or CadCanvasToolMode.ArcCenterStartLength)
            {
                if (values[0] <= 0 || World(values[0]) > center.DistanceTo(start) * 2) return false;
                point = start + InputDirection(start, point) * World(values[0]);
            }
            else
            {
                if (!IsInputSweep(values[0])) return false;
                point = GetArcPoint(center, center.DistanceTo(start), AngleFrom(center, start) + values[0] * Math.PI / 180);
            }
        }
        else
        {
            var midpoint = Midpoint(first, next);
            var normal = InputDirection(first, next).Perpendicular();
            var side = (point - midpoint).Dot(normal) < 0 ? -1 : 1;
            switch (CadCanvasToolMode)
            {
                case CadCanvasToolMode.ArcStartEndAngle:
                    if (!IsInputSweep(values[0])) return false;
                    point = midpoint + normal * (side * first.DistanceTo(next) * .5 * Math.Tan(values[0] * Math.PI / 720));
                    break;
                case CadCanvasToolMode.ArcStartEndRadius:
                    if (values[0] <= 0 || World(values[0]) < first.DistanceTo(next) * .5) return false;
                    point = first + normal * (side * World(values[0]));
                    break;
                case CadCanvasToolMode.ArcStartEndDirection:
                    var angle = values[0] % 360 * Math.PI / 180;
                    point = first + new CadVectorD(Math.Cos(angle), Math.Sin(angle)) * Math.Max(1, first.DistanceTo(_dynamicInputPointer));
                    break;
            }
        }
        return TryCreateArcFromMode(CadCanvasToolMode, first, next, point, out _);
    }

    private static bool TryInputCircleCenter(CadPointD first, CadPointD second, double radius, CadPointD reference, out CadPointD center)
    {
        center = default;
        var half = first.DistanceTo(second) * .5;
        if (!double.IsFinite(radius) || radius <= 0 || half <= 1e-9 || radius < half) return false;
        var normal = InputDirection(first, second).Perpendicular();
        var midpoint = Midpoint(first, second);
        var height = radius * Math.Sqrt(Math.Max(0, 1 - (half / radius) * (half / radius)));
        center = midpoint + normal * ((reference - midpoint).Dot(normal) < 0 ? -height : height);
        return double.IsFinite(center.X) && double.IsFinite(center.Y);
    }

    private bool TryResolveEllipseDynamicInput(double[] values, out CadPointD point, out CadDrawingSessionState? resolvedState)
    {
        point = _dynamicInputPointer;
        resolvedState = null;
        var radiusX = CadUnitConversion.ToMillimeters(values[0], DocumentUnit);
        var radiusY = CadUnitConversion.ToMillimeters(values[1], DocumentUnit);
        if (!IsValidEllipseGeometry(radiusX, radiusY)) return false;
        var raw = GetDynamicEllipse(_drawingState, point);
        var copy = _drawingState.Clone();
        var points = copy.PendingEllipsePoints;
        var axis = points[1] - points[0];
        var horizontal = Math.Abs(axis.X) >= Math.Abs(axis.Y);
        var factor = CadCanvasToolMode == CadCanvasToolMode.EllipseCenter ? 1 : 2;
        var axisRadius = horizontal ? radiusX : radiusY;
        var component = horizontal ? Math.Abs(axis.X) : Math.Abs(axis.Y);
        if (component <= 1e-9) return false;
        points[1] = points[0] + axis * (axisRadius * factor / component);
        var center = CadCanvasToolMode == CadCanvasToolMode.EllipseCenter ? points[0] : Midpoint(points[0], points[1]);
        var originalOther = points.Count == 2 ? point : points[2];
        var other = horizontal ? new CadPointD(center.X, center.Y + (originalOther.Y < raw.Center.Y ? -radiusY : radiusY))
            : new CadPointD(center.X + (originalOther.X < raw.Center.X ? -radiusX : radiusX), center.Y);
        if (points.Count == 2) point = other;
        else
        {
            points[2] = other;
            var startAngle = points.Count == 4 ? EllipseAngleFrom(raw.Center, raw.RadiusX, raw.RadiusY, points[3]) : 0;
            if (points.Count == 4) points[3] = GetEllipsePoint(center, radiusX, radiusY, startAngle);
            var angle = values[2] * Math.PI / 180;
            if (points.Count == 4)
            {
                if (!IsInputSweep(values[2])) return false;
                angle += startAngle;
            }
            point = GetEllipsePoint(center, radiusX, radiusY, angle);
        }
        resolvedState = copy;
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    public IReadOnlyList<CadDynamicInputMeasurement> DynamicInputScreenMeasurements
    {
        get
        {
            var point = ResolveDynamicInputPreview(_dynamicInputPointer);
            var state = _dynamicInputResolvedState ?? _drawingState;
            var spans = new List<CadDynamicInputMeasurement>();
            void Add(string key, CadPointD start, CadPointD end, int stack = 0) => spans.Add(new(key, WorldToScreen(start), WorldToScreen(end), stack));
            if (_dynamicInputKind == DynamicInputKind.Grip && GripDynamicInputAnchor is { } gripAnchor)
            {
                var keys=GetGripDynamicInputKeys();
                if(keys[0] is "Length" or "Radius" or "AxisX" or "AxisY") Add(keys[0],gripAnchor,point);
                if(keys.Length>1 && keys[1]=="Angle") Add("Angle",gripAnchor,point,1);
                if(keys[0]=="Width" && GripEntity is Direct2dCad.Db.Data.Entities.CadRectangle rectangle)
                {
                    var local=CadMatrixD.CreateRotation(-rectangle.RotationRadians).TransformVector(point-gripAnchor);
                    var corner=gripAnchor+CadMatrixD.CreateRotation(rectangle.RotationRadians).TransformVector(new(local.X,0));
                    Add("Width",gripAnchor,corner); Add("Height",corner,point);
                }
            }
            else if (_dynamicInputKind == DynamicInputKind.Curve)
            {
                if (IsEllipseDrawing)
                {
                    var ellipse = GetDynamicEllipse(state, point);
                    Add("AxisX", ellipse.Center, new(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y));
                    Add("AxisY", ellipse.Center, new(ellipse.Center.X, ellipse.Center.Y + ellipse.RadiusY));
                    if (state.PendingEllipsePoints.Count > 2) Add(state.PendingEllipsePoints.Count == 3 ? "StartAngle" : "Angle", ellipse.Center, point, 1);
                }
                else if (CadCanvasToolMode == CadCanvasToolMode.CircleThreePoint &&
                    TryCreateCircleFromThreePoints(state.PendingWorldPoint!.Value, state.PendingCircleSecondPoint!.Value, point, out var center, out _))
                    Add("Radius", center, point);
                else if (IsCenterArcDrawing && state.PendingArcStartPoint is null)
                    Add("Radius", state.PendingWorldPoint!.Value, point);
                else if (TryGetDynamicArc(state, point, out var arc))
                {
                    var start = GetArcPoint(arc.Center, arc.Radius, arc.StartAngleRadians);
                    var end = GetArcPoint(arc.Center, arc.Radius, arc.StartAngleRadians + arc.SweepAngleRadians);
                    foreach (var inputField in DynamicInputFields)
                        switch (inputField.Key)
                        {
                            case "Radius": Add(inputField.Key, arc.Center, start); break;
                            case "Angle": Add(inputField.Key, arc.Center, end, DynamicInputFields.Count > 1 ? 1 : 0); break;
                            case "Length": Add(inputField.Key, start, end); break;
                            case "Direction": Add(inputField.Key, state.PendingWorldPoint!.Value, point); break;
                        }
                }
            }
            else if (DynamicInputAnchor is { } anchor)
            {
                var delta = point - anchor;
                if (_dynamicInputKind == DynamicInputKind.Radius) Add("Radius", anchor, point);
                if (_dynamicInputKind == DynamicInputKind.Diameter)
                {
                    if (CadCanvasToolMode == CadCanvasToolMode.CircleTwoPoint) Add("Diameter", anchor, point);
                    else Add("Diameter", anchor - delta * .5, anchor + delta * .5);
                }
                if (_dynamicInputKind == DynamicInputKind.Polar && CadCanvasToolMode != CadCanvasToolMode.Spline) Add("Length", anchor, point);
            }
            return spans;
        }
    }
}
