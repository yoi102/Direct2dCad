using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly record struct CadContinueArcBase(
    bool HasValue,
    CadPointD Start,
    CadVectorD Tangent);

internal readonly record struct CadDrawingTextRequest(
    string Text,
    StyleId? TextStyleId,
    double InvertedMarginFactor,
    double RotationRadians);

internal sealed class CadDrawingClickHandler(
    CadCanvasToolMode toolMode,
    CadDrawingSessionState state,
    CadDrawingEntityCreator creator,
    CadMultiPointDrawingPreviewBuilder multiPointPreviewBuilder,
    Func<CadContinueArcBase> continueArcBaseResolver,
    Func<CadDrawingTextRequest> textRequestFactory)
{
    public bool HandleClick(CadPointD world)
    {
        if (!double.IsFinite(world.X) || !double.IsFinite(world.Y))
            return false;

        switch (toolMode)
        {
            case CadCanvasToolMode.Line:
                return HandleLineClick(world);

            case CadCanvasToolMode.CircleCenterRadius:
                return HandleCircleCenterRadiusClick(world);

            case CadCanvasToolMode.CircleCenterDiameter:
                return HandleCircleCenterDiameterClick(world);

            case CadCanvasToolMode.CircleTwoPoint:
                return HandleCircleTwoPointClick(world);

            case CadCanvasToolMode.CircleThreePoint:
                return HandleCircleThreePointClick(world);

            case CadCanvasToolMode.EllipseCenter:
            case CadCanvasToolMode.EllipseAxisEnd:
            case CadCanvasToolMode.EllipseArc:
                return HandleEllipseDrawingClick(world);

            case CadCanvasToolMode.ArcThreePoint:
            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcStartEndAngle:
            case CadCanvasToolMode.ArcStartEndDirection:
            case CadCanvasToolMode.ArcStartEndRadius:
            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcCenterStartLength:
            case CadCanvasToolMode.ArcContinue:
                return HandleArcDrawingClick(world);

            case CadCanvasToolMode.Rectangle:
                return HandleRectangleClick(world);

            case CadCanvasToolMode.Polyline:
                AddPolylineVertexOrComplete(world);
                return true;

            case CadCanvasToolMode.Polygon:
                AddPolygonVertexOrComplete(world);
                return true;

            case CadCanvasToolMode.Spline:
                AddSplineFitPointOrComplete(world);
                return true;

            case CadCanvasToolMode.Text:
                var text = textRequestFactory();
                creator.AddText(
                    world,
                    text.Text,
                    text.TextStyleId,
                    text.InvertedMarginFactor,
                    text.RotationRadians);
                return true;

            case CadCanvasToolMode.SetOrigin:
                creator.SetOriginPosition(world);
                return true;

            default:
                return false;
        }
    }

    public bool CompleteCurrentDrawing()
    {
        return toolMode switch
        {
            CadCanvasToolMode.Polyline => CompletePolyline(),
            CadCanvasToolMode.Polygon => CompletePolygon(),
            CadCanvasToolMode.Spline => CompleteSpline(),
            _ => false
        };
    }

    private bool HandleLineClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        if (!IsValidCircleGeometry(state.PendingWorldPoint.Value.DistanceTo(world)))
            return false;
        creator.AddLine(state.PendingWorldPoint.Value, world);
        state.PendingWorldPoint = null;
        return true;
    }

    private bool HandleRectangleClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        var bounds = CadRectD.FromLTRB(
            state.PendingWorldPoint.Value.X,
            state.PendingWorldPoint.Value.Y,
            world.X,
            world.Y);
        if (!CadDrawingEntityCreator.IsValidRectangleBounds(bounds))
            return false;
        creator.AddRectangleIfValid(bounds);
        state.PendingWorldPoint = null;
        return true;
    }

    private bool HandleCircleCenterRadiusClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        var center = state.PendingWorldPoint.Value;
        var radius = center.DistanceTo(world);
        if (!IsValidCircleGeometry(radius))
            return false;
        creator.AddCircleIfValid(center, radius);
        state.PendingWorldPoint = null;
        return true;
    }

    private bool HandleCircleCenterDiameterClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        var center = state.PendingWorldPoint.Value;
        var radius = center.DistanceTo(world) * 0.5;
        if (!IsValidCircleGeometry(radius))
            return false;
        creator.AddCircleIfValid(center, radius);
        state.PendingWorldPoint = null;
        return true;
    }

    private bool HandleCircleTwoPointClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        if (!TryCreateCircleFromDiameterPoints(
            state.PendingWorldPoint.Value,
            world,
            out var center,
            out var radius))
            return false;

        creator.AddCircleIfValid(center, radius);
        state.PendingWorldPoint = null;
        return true;
    }

    private bool HandleCircleThreePointClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        if (state.PendingCircleSecondPoint is null)
        {
            if (!IsValidCircleGeometry(state.PendingWorldPoint.Value.DistanceTo(world)))
                return false;
            state.PendingCircleSecondPoint = world;
            return true;
        }

        if (!TryCreateCircleFromThreePoints(
            state.PendingWorldPoint.Value,
            state.PendingCircleSecondPoint.Value,
            world,
            out var center,
            out var radius))
            return false;

        creator.AddCircleIfValid(center, radius);
        state.PendingWorldPoint = null;
        state.PendingCircleSecondPoint = null;
        return true;
    }

    private bool HandleArcDrawingClick(CadPointD world)
    {
        if (toolMode == CadCanvasToolMode.ArcContinue)
        {
            var arcBase = continueArcBaseResolver();
            if (!arcBase.HasValue ||
                !TryCreateArcFromStartEndTangent(arcBase.Start, world, arcBase.Tangent, out var continueGeometry))
                return false;
            creator.AddArcIfValid(continueGeometry);
            return true;
        }

        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return true;
        }

        if (state.PendingArcStartPoint is null)
        {
            if (!IsValidCircleGeometry(state.PendingWorldPoint.Value.DistanceTo(world)))
                return false;
            state.PendingArcStartPoint = world;
            return true;
        }

        if (!TryCreateArcFromMode(
            toolMode,
            state.PendingWorldPoint.Value,
            state.PendingArcStartPoint.Value,
            world,
            out var geometry))
            return false;

        creator.AddArcIfValid(geometry);
        state.PendingWorldPoint = null;
        state.PendingArcStartPoint = null;
        return true;
    }

    private bool HandleEllipseDrawingClick(CadPointD world)
    {
        var points = state.PendingEllipsePoints;
        if (points.Count == 1 && !IsValidCircleGeometry(points[0].DistanceTo(world)))
            return false;
        if (points.Count == 2)
        {
            var valid = toolMode == CadCanvasToolMode.EllipseCenter
                ? TryCreateEllipseFromCenter(points[0], points[1], world, out _)
                : TryCreateEllipseFromAxisEnd(points[0], points[1], world, out _);
            if (!valid)
                return false;
        }
        if (points.Count >= 3 &&
            world.DistanceTo(Midpoint(points[0], points[1])) <= double.Epsilon)
            return false;

        state.PendingEllipsePoints.Add(world);

        switch (toolMode)
        {
            case CadCanvasToolMode.EllipseCenter when state.PendingEllipsePoints.Count == 3:
                if (TryCreateEllipseFromCenter(
                    state.PendingEllipsePoints[0],
                    state.PendingEllipsePoints[1],
                    state.PendingEllipsePoints[2],
                    out var centerGeometry))
                {
                    creator.AddEllipseIfValid(
                        centerGeometry.Center,
                        centerGeometry.RadiusX,
                        centerGeometry.RadiusY);
                }

                state.PendingEllipsePoints.Clear();
                break;

            case CadCanvasToolMode.EllipseAxisEnd when state.PendingEllipsePoints.Count == 3:
                if (TryCreateEllipseFromAxisEnd(
                    state.PendingEllipsePoints[0],
                    state.PendingEllipsePoints[1],
                    state.PendingEllipsePoints[2],
                    out var axisGeometry))
                {
                    creator.AddEllipseIfValid(
                        axisGeometry.Center,
                        axisGeometry.RadiusX,
                        axisGeometry.RadiusY);
                }

                state.PendingEllipsePoints.Clear();
                break;

            case CadCanvasToolMode.EllipseArc when state.PendingEllipsePoints.Count == 5:
                if (TryCreateEllipseArcFromPoints(
                    state.PendingEllipsePoints[0],
                    state.PendingEllipsePoints[1],
                    state.PendingEllipsePoints[2],
                    state.PendingEllipsePoints[3],
                    state.PendingEllipsePoints[4],
                    out var arcGeometry))
                {
                    creator.AddEllipseArcIfValid(arcGeometry);
                }
                else
                {
                    // Only reject the last point; keep the ellipse and its start angle.
                    points.RemoveAt(points.Count - 1);
                    return false;
                }

                state.PendingEllipsePoints.Clear();
                break;
        }
        return true;
    }

    private void AddPolylineVertexOrComplete(CadPointD world)
    {
        if (multiPointPreviewBuilder.ShouldCompletePolyline(state.PendingPolylinePoints, world))
        {
            CompletePolyline();
            return;
        }

        AddDistinctPoint(state.PendingPolylinePoints, world);
    }

    private bool CompletePolyline()
    {
        if (state.PendingPolylinePoints.Count < 2)
            return false;

        creator.AddPolyline(state.PendingPolylinePoints);
        state.PendingPolylinePoints.Clear();
        return true;
    }

    private void AddSplineFitPointOrComplete(CadPointD world)
    {
        if (multiPointPreviewBuilder.ShouldCompleteSpline(state.PendingSplinePoints, world))
        {
            CompleteSpline();
            return;
        }

        AddDistinctPoint(state.PendingSplinePoints, world);
    }

    private bool CompleteSpline()
    {
        if (state.PendingSplinePoints.Count < 2)
            return false;

        creator.AddSpline(state.PendingSplinePoints);
        state.PendingSplinePoints.Clear();
        return true;
    }

    private void AddPolygonVertexOrComplete(CadPointD world)
    {
        if (multiPointPreviewBuilder.ShouldClosePolygon(state.PendingPolygonPoints, world))
        {
            CompletePolygon();
            return;
        }

        AddDistinctPoint(state.PendingPolygonPoints, world);
    }

    private bool CompletePolygon()
    {
        if (state.PendingPolygonPoints.Count < 3)
            return false;

        creator.AddPolygon(state.PendingPolygonPoints);
        state.PendingPolygonPoints.Clear();
        return true;
    }

    private static void AddDistinctPoint(List<CadPointD> points, CadPointD point)
    {
        if (points.Count == 0 || !points[^1].NearEquals(point))
            points.Add(point);
    }
}
