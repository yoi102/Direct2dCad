using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using static Direct2dCad.ViewModels.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Drawing;

internal readonly record struct CadContinueArcBase(
    bool HasValue,
    CadPointD Start,
    CadVectorD Tangent);

internal readonly record struct CadDrawingTextRequest(
    string Text,
    StyleId? TextStyleId,
    double InvertedMarginFactor);

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
        switch (toolMode)
        {
            case CadCanvasToolMode.Line:
                HandleLineClick(world);
                return true;

            case CadCanvasToolMode.CircleCenterRadius:
                HandleCircleCenterRadiusClick(world);
                return true;

            case CadCanvasToolMode.CircleCenterDiameter:
                HandleCircleCenterDiameterClick(world);
                return true;

            case CadCanvasToolMode.CircleTwoPoint:
                HandleCircleTwoPointClick(world);
                return true;

            case CadCanvasToolMode.CircleThreePoint:
                HandleCircleThreePointClick(world);
                return true;

            case CadCanvasToolMode.EllipseCenter:
            case CadCanvasToolMode.EllipseAxisEnd:
            case CadCanvasToolMode.EllipseArc:
                HandleEllipseDrawingClick(world);
                return true;

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
                HandleArcDrawingClick(world);
                return true;

            case CadCanvasToolMode.Rectangle:
                HandleRectangleClick(world);
                return true;

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
                creator.AddText(world, text.Text, text.TextStyleId, text.InvertedMarginFactor);
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

    private void HandleLineClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        creator.AddLine(state.PendingWorldPoint.Value, world);
        state.PendingWorldPoint = null;
    }

    private void HandleRectangleClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        var bounds = CadRectD.FromLTRB(
            state.PendingWorldPoint.Value.X,
            state.PendingWorldPoint.Value.Y,
            world.X,
            world.Y);
        creator.AddRectangleIfValid(bounds);
        state.PendingWorldPoint = null;
    }

    private void HandleCircleCenterRadiusClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        var center = state.PendingWorldPoint.Value;
        creator.AddCircleIfValid(center, center.DistanceTo(world));
        state.PendingWorldPoint = null;
    }

    private void HandleCircleCenterDiameterClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        var center = state.PendingWorldPoint.Value;
        creator.AddCircleIfValid(center, center.DistanceTo(world) * 0.5);
        state.PendingWorldPoint = null;
    }

    private void HandleCircleTwoPointClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        if (TryCreateCircleFromDiameterPoints(
            state.PendingWorldPoint.Value,
            world,
            out var center,
            out var radius))
        {
            creator.AddCircleIfValid(center, radius);
        }

        state.PendingWorldPoint = null;
    }

    private void HandleCircleThreePointClick(CadPointD world)
    {
        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        if (state.PendingCircleSecondPoint is null)
        {
            if (state.PendingWorldPoint.Value.DistanceTo(world) > double.Epsilon)
                state.PendingCircleSecondPoint = world;
            return;
        }

        if (TryCreateCircleFromThreePoints(
            state.PendingWorldPoint.Value,
            state.PendingCircleSecondPoint.Value,
            world,
            out var center,
            out var radius))
        {
            creator.AddCircleIfValid(center, radius);
        }

        state.PendingWorldPoint = null;
        state.PendingCircleSecondPoint = null;
    }

    private void HandleArcDrawingClick(CadPointD world)
    {
        if (toolMode == CadCanvasToolMode.ArcContinue)
        {
            var arcBase = continueArcBaseResolver();
            if (arcBase.HasValue &&
                TryCreateArcFromStartEndTangent(arcBase.Start, world, arcBase.Tangent, out var continueGeometry))
            {
                creator.AddArcIfValid(continueGeometry);
            }

            return;
        }

        if (state.PendingWorldPoint is null)
        {
            state.PendingWorldPoint = world;
            return;
        }

        if (state.PendingArcStartPoint is null)
        {
            if (state.PendingWorldPoint.Value.DistanceTo(world) > double.Epsilon)
                state.PendingArcStartPoint = world;
            return;
        }

        if (TryCreateArcFromMode(
            toolMode,
            state.PendingWorldPoint.Value,
            state.PendingArcStartPoint.Value,
            world,
            out var geometry))
        {
            creator.AddArcIfValid(geometry);
        }

        state.PendingWorldPoint = null;
        state.PendingArcStartPoint = null;
    }

    private void HandleEllipseDrawingClick(CadPointD world)
    {
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

                state.PendingEllipsePoints.Clear();
                break;
        }
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
