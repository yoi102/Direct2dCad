using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Geometry;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadArcDrawingPreviewBuilder(
    CadCanvasToolMode toolMode,
    CadTransientMeasurementBuilder measurementBuilder)
{
    public void AddPreview(
        List<CadTransientItem> items,
        CadPointD? pendingWorldPoint,
        CadPointD? pendingArcStartPoint,
        CadPointD? continueStart,
        CadVectorD? continueTangent,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (toolMode == CadCanvasToolMode.ArcContinue)
        {
            AddContinuePreview(items, continueStart, continueTangent, mouseWorld, style, auxiliaryStyle);
            return;
        }

        if (pendingWorldPoint is not { } first)
            return;

        if (pendingArcStartPoint is not { } second)
        {
            AddArcFirstStagePreview(items, first, mouseWorld, auxiliaryStyle);
            return;
        }

        if (!TryCreateArcFromMode(toolMode, first, second, mouseWorld, out var arcGeometry))
        {
            items.Add(new CadTransientLine(first, second, auxiliaryStyle));
            items.Add(new CadTransientLine(second, mouseWorld, auxiliaryStyle));
            return;
        }

        AddArcGeometryPreview(items, arcGeometry, style, auxiliaryStyle);
        AddArcModeAuxiliaryPreview(items, first, second, mouseWorld, arcGeometry, auxiliaryStyle);
        AddArcModeMeasurementPreview(items, first, second, mouseWorld, arcGeometry, auxiliaryStyle);
    }

    private void AddContinuePreview(
        List<CadTransientItem> items,
        CadPointD? continueStart,
        CadVectorD? continueTangent,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (continueStart is not { } start ||
            continueTangent is not { } tangent ||
            !TryCreateArcFromStartEndTangent(start, mouseWorld, tangent, out var geometry))
        {
            return;
        }

        AddArcGeometryPreview(items, geometry, style, auxiliaryStyle);
        items.Add(new CadTransientLine(start, start + tangent.Normalize() * geometry.Radius * 0.35, auxiliaryStyle));
        AddArcMeasurementPreview(
            items,
            start,
            mouseWorld,
            $"R {measurementBuilder.FormatLength(geometry.Radius)}",
            auxiliaryStyle);
    }

    private void AddArcFirstStagePreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD mouseWorld,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientLine(first, mouseWorld, auxiliaryStyle));

        switch (toolMode)
        {
            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcStartCenterLength:
                var startCenterRadius = first.DistanceTo(mouseWorld);
                if (startCenterRadius > double.Epsilon)
                    items.Add(new CadTransientCircle(mouseWorld, startCenterRadius, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcCenterStartLength:
                var centerStartRadius = first.DistanceTo(mouseWorld);
                if (centerStartRadius > double.Epsilon)
                    items.Add(new CadTransientCircle(first, centerStartRadius, auxiliaryStyle));
                break;
        }
    }

    private static void AddArcGeometryPreview(
        List<CadTransientItem> items,
        ArcDrawingGeometry geometry,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientArc(
            geometry.Center,
            geometry.Radius,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            style));

        items.Add(new CadTransientLine(
            geometry.Center,
            GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians),
            auxiliaryStyle));
        items.Add(new CadTransientLine(
            geometry.Center,
            GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians + geometry.SweepAngleRadians),
            auxiliaryStyle));
    }

    private void AddArcModeAuxiliaryPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        ArcDrawingGeometry geometry,
        CadTransientStyle auxiliaryStyle)
    {
        switch (toolMode)
        {
            case CadCanvasToolMode.ArcThreePoint:
                items.Add(new CadTransientLine(first, second, auxiliaryStyle));
                items.Add(new CadTransientLine(second, third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartEndDirection:
                items.Add(new CadTransientLine(first, third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartEndAngle:
            case CadCanvasToolMode.ArcStartEndRadius:
                items.Add(new CadTransientLine(first, second, auxiliaryStyle));
                items.Add(new CadTransientLine(Midpoint(first, second), third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcCenterStartLength:
                items.Add(new CadTransientLine(GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians), third, auxiliaryStyle));
                break;
        }
    }

    private void AddArcModeMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        ArcDrawingGeometry geometry,
        CadTransientStyle auxiliaryStyle)
    {
        var startPoint = GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians);
        var endPoint = GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians + geometry.SweepAngleRadians);

        switch (toolMode)
        {
            case CadCanvasToolMode.ArcThreePoint:
                break;

            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcStartEndAngle:
                AddArcMeasurementPreview(
                    items,
                    geometry.Center,
                    endPoint,
                    $"A {measurementBuilder.FormatAngleDegrees(Math.Abs(geometry.SweepAngleRadians))}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcCenterStartLength:
                AddArcMeasurementPreview(
                    items,
                    startPoint,
                    endPoint,
                    $"L {measurementBuilder.FormatLength(startPoint.DistanceTo(endPoint))}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartEndRadius:
                AddArcMeasurementPreview(
                    items,
                    geometry.Center,
                    startPoint,
                    $"R {measurementBuilder.FormatLength(geometry.Radius)}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartEndDirection:
                AddArcMeasurementPreview(
                    items,
                    first,
                    third,
                    $"D {measurementBuilder.FormatAngleDegrees(NormalizePositive(AngleFrom(first, third)))}",
                    auxiliaryStyle);
                break;
        }
    }

    private void AddArcMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        measurementBuilder.AddText(items, lineStart, lineEnd, text, style);
    }
}
