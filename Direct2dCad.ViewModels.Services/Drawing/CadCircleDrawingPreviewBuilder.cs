using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly struct CadCircleDrawingPreviewBuilder(
    CadCanvasToolMode toolMode,
    CadTransientMeasurementBuilder measurementBuilder)
{
    public void AddPreview(
        List<CadTransientItem> items,
        CadPointD? pendingWorldPoint,
        CadPointD? pendingCircleSecondPoint,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        switch (toolMode)
        {
            case CadCanvasToolMode.CircleCenterRadius:
                if (pendingWorldPoint is { } centerRadiusCenter)
                    AddCircleCenterRadiusPreview(items, centerRadiusCenter, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleCenterDiameter:
                if (pendingWorldPoint is { } centerDiameterCenter)
                    AddCircleCenterDiameterPreview(items, centerDiameterCenter, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleTwoPoint:
                if (pendingWorldPoint is { } firstDiameterPoint)
                    AddCircleTwoPointPreview(items, firstDiameterPoint, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleThreePoint:
                if (pendingWorldPoint is { } firstPoint &&
                    pendingCircleSecondPoint is { } secondPoint)
                {
                    AddCircleThreePointPreview(items, firstPoint, secondPoint, mouseWorld, style, auxiliaryStyle);
                }
                else if (pendingWorldPoint is { } firstOnlyPoint)
                {
                    items.Add(new CadTransientLine(firstOnlyPoint, mouseWorld, auxiliaryStyle));
                }
                break;
        }
    }

    private void AddCircleCenterRadiusPreview(
        List<CadTransientItem> items,
        CadPointD center,
        CadPointD edge,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = center.DistanceTo(edge);
        if (!IsValidCircleGeometry(radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        items.Add(new CadTransientLine(center, edge, auxiliaryStyle));
        AddCircleMeasurementPreview(items, center, edge, $"R {measurementBuilder.FormatLengthLabel(radius)}", auxiliaryStyle);
    }

    private void AddCircleCenterDiameterPreview(
        List<CadTransientItem> items,
        CadPointD center,
        CadPointD diameterPoint,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = center.DistanceTo(diameterPoint) * 0.5;
        if (!IsValidCircleGeometry(radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        var direction = diameterPoint - center;
        var unit = direction.Normalize();
        if (unit == CadVectorD.Zero)
            return;

        var start = center - unit * radius;
        var end = center + unit * radius;
        items.Add(new CadTransientLine(start, end, auxiliaryStyle));
        AddCircleMeasurementPreview(items, start, end, $"D {measurementBuilder.FormatLengthLabel(radius * 2.0)}", auxiliaryStyle);
    }

    private void AddCircleTwoPointPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateCircleFromDiameterPoints(first, second, out var center, out var radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        items.Add(new CadTransientLine(first, second, auxiliaryStyle));
        AddCircleMeasurementPreview(items, first, second, $"D {measurementBuilder.FormatLengthLabel(radius * 2.0)}", auxiliaryStyle);
    }

    private void AddCircleThreePointPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientLine(first, second, auxiliaryStyle));
        items.Add(new CadTransientLine(second, third, auxiliaryStyle));

        if (!TryCreateCircleFromThreePoints(first, second, third, out var center, out var radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        AddCircleMeasurementPreview(
            items,
            center,
            third,
            $"R {measurementBuilder.FormatLengthLabel(radius)}",
            auxiliaryStyle);
    }

    private void AddCircleMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        measurementBuilder.AddText(items, lineStart, lineEnd, text, style);
    }
}
