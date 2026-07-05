using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Styling;
using Direct2dCad.ViewModels.Text;

namespace Direct2dCad.ViewModels.Drawing;

internal sealed class CadDrawingPreviewDispatcher(
    CadCanvasToolMode toolMode,
    CadDrawingSessionState state,
    CadDrawingDefaults defaults,
    CadDrawingStyleResolver styleResolver,
    CadPreviewStyleService previewStyleService,
    CadTransientMeasurementBuilder measurementBuilder,
    CadMultiPointDrawingPreviewBuilder multiPointPreviewBuilder,
    CadTextMeasurementService textMeasurementService,
    CadDocument document,
    CadViewport viewport,
    Func<CadContinueArcBase> continueArcBaseResolver,
    Func<CadDrawingTextRequest> textRequestFactory)
{
    public void AddPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        switch (toolMode)
        {
            case CadCanvasToolMode.Line when state.PendingWorldPoint is { } start:
                items.Add(new CadTransientLine(start, mouseWorld, styleResolver.CreateLineTransientStyle()));
                break;

            case CadCanvasToolMode.CircleCenterRadius:
            case CadCanvasToolMode.CircleCenterDiameter:
            case CadCanvasToolMode.CircleTwoPoint:
            case CadCanvasToolMode.CircleThreePoint:
                AddCirclePreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.EllipseCenter:
            case CadCanvasToolMode.EllipseAxisEnd:
            case CadCanvasToolMode.EllipseArc:
                AddEllipsePreview(items, mouseWorld);
                break;

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
                AddArcPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.Rectangle when state.PendingWorldPoint is { } firstCorner:
                AddRectanglePreview(items, firstCorner, mouseWorld);
                break;

            case CadCanvasToolMode.Polyline:
                multiPointPreviewBuilder.AddPolylinePreview(items, state.PendingPolylinePoints, mouseWorld);
                break;

            case CadCanvasToolMode.Polygon:
                multiPointPreviewBuilder.AddPolygonPreview(items, state.PendingPolygonPoints, mouseWorld);
                break;

            case CadCanvasToolMode.Spline:
                multiPointPreviewBuilder.AddSplinePreview(items, state.PendingSplinePoints, mouseWorld);
                break;

            case CadCanvasToolMode.Text:
                AddTextPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.SetOrigin:
                new CadOriginPositionPreviewBuilder(
                    document.ViewSettings.Origin,
                    viewport.Zoom).AddPreview(items, mouseWorld);
                break;
        }
    }

    private void AddCirclePreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        new CadCircleDrawingPreviewBuilder(toolMode, measurementBuilder).AddPreview(
            items,
            state.PendingWorldPoint,
            state.PendingCircleSecondPoint,
            mouseWorld,
            styleResolver.CreateCircleTransientStyle(),
            previewStyleService.CreateDrawingAuxiliaryStyle(defaults.CircleStrokeColor));
    }

    private void AddEllipsePreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        new CadEllipseDrawingPreviewBuilder(toolMode, measurementBuilder).AddPreview(
            items,
            state.PendingEllipsePoints,
            mouseWorld,
            styleResolver.CreateEllipseTransientStyle(),
            previewStyleService.CreateDrawingAuxiliaryStyle(defaults.EllipseStrokeColor));
    }

    private void AddArcPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        var arcBase = continueArcBaseResolver();
        CadPointD? continueStart = arcBase.HasValue ? arcBase.Start : null;
        CadVectorD? continueTangent = arcBase.HasValue ? arcBase.Tangent : null;

        new CadArcDrawingPreviewBuilder(toolMode, measurementBuilder).AddPreview(
            items,
            state.PendingWorldPoint,
            state.PendingArcStartPoint,
            continueStart,
            continueTangent,
            mouseWorld,
            styleResolver.CreateArcTransientStyle(),
            previewStyleService.CreateDrawingAuxiliaryStyle(defaults.ArcStrokeColor));
    }

    private void AddRectanglePreview(List<CadTransientItem> items, CadPointD firstCorner, CadPointD mouseWorld)
    {
        var bounds = CadRectD.FromLTRB(firstCorner.X, firstCorner.Y, mouseWorld.X, mouseWorld.Y);
        if (!CadDrawingEntityCreator.IsValidRectangleBounds(bounds))
            return;

        items.Add(new CadTransientRectangle(
            bounds,
            styleResolver.CreateRectangleTransientStyle(),
            styleResolver.ResolveRectangleCornerRadiusX(bounds),
            styleResolver.ResolveRectangleCornerRadiusY(bounds)));
    }

    private void AddTextPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        var text = textRequestFactory();
        var drawingHeight = textMeasurementService.ResolveTextBoxHeight(text.Text, text.TextStyleId);
        items.Add(new CadTransientText(
            text.Text,
            mouseWorld,
            drawingHeight,
            textMeasurementService.CreateTextBounds(text.Text, mouseWorld, drawingHeight, text.TextStyleId),
            styleResolver.CreateTextTransientStyle(),
            defaults.TextInverted,
            text.InvertedMarginFactor,
            text.TextStyleId));
    }
}
