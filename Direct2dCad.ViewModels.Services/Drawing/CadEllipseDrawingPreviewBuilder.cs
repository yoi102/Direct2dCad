using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Geometry;
using static Direct2dCad.ViewModels.Services.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly struct CadEllipseDrawingPreviewBuilder(
    CadCanvasToolMode toolMode,
    CadTransientMeasurementBuilder measurementBuilder)
{
    public void AddPreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingEllipsePoints,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        switch (toolMode)
        {
            case CadCanvasToolMode.EllipseCenter:
                AddEllipseCenterPreview(items, pendingEllipsePoints, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.EllipseAxisEnd:
                AddEllipseAxisEndPreview(items, pendingEllipsePoints, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.EllipseArc:
                AddEllipseArcPreview(items, pendingEllipsePoints, mouseWorld, style, auxiliaryStyle);
                break;
        }
    }

    private void AddEllipseCenterPreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingEllipsePoints,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (pendingEllipsePoints.Count == 0)
            return;

        var center = pendingEllipsePoints[0];
        items.Add(new CadTransientLine(center, mouseWorld, auxiliaryStyle));

        if (pendingEllipsePoints.Count < 2)
            return;

        if (!TryCreateEllipseFromCenter(center, pendingEllipsePoints[1], mouseWorld, out var geometry))
            return;

        items.Add(new CadTransientEllipse(geometry.Center, geometry.RadiusX, geometry.RadiusY, style));
        AddEllipseRadiusMeasurements(items, geometry, auxiliaryStyle);
    }

    private void AddEllipseAxisEndPreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingEllipsePoints,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (pendingEllipsePoints.Count == 0)
            return;

        items.Add(new CadTransientLine(pendingEllipsePoints[0], mouseWorld, auxiliaryStyle));

        if (pendingEllipsePoints.Count < 2)
            return;

        if (!TryCreateEllipseFromAxisEnd(pendingEllipsePoints[0], pendingEllipsePoints[1], mouseWorld, out var geometry))
            return;

        items.Add(new CadTransientEllipse(geometry.Center, geometry.RadiusX, geometry.RadiusY, style));
        AddEllipseRadiusMeasurements(items, geometry, auxiliaryStyle);
    }

    private void AddEllipseArcPreview(
        List<CadTransientItem> items,
        IReadOnlyList<CadPointD> pendingEllipsePoints,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (pendingEllipsePoints.Count == 0)
            return;

        var previewPoints = pendingEllipsePoints.Concat([mouseWorld]).ToArray();

        if (previewPoints.Length >= 2)
            items.Add(new CadTransientLine(previewPoints[0], previewPoints[1], auxiliaryStyle));

        if (previewPoints.Length < 3 ||
            !TryCreateEllipseFromAxisEnd(previewPoints[0], previewPoints[1], previewPoints[2], out var ellipse))
        {
            return;
        }

        items.Add(new CadTransientEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, auxiliaryStyle));
        AddEllipseRadiusMeasurements(items, ellipse, auxiliaryStyle);

        if (previewPoints.Length >= 4)
        {
            var startAngle = EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, previewPoints[3]);
            var startPoint = GetEllipsePoint(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, startAngle);
            items.Add(new CadTransientLine(ellipse.Center, startPoint, auxiliaryStyle));
        }

        if (previewPoints.Length < 5)
            return;

        if (!TryCreateEllipseArcFromPoints(
            previewPoints[0],
            previewPoints[1],
            previewPoints[2],
            previewPoints[3],
            previewPoints[4],
            out var arc))
        {
            return;
        }

        items.Add(new CadTransientEllipseArc(
            arc.Center,
            arc.RadiusX,
            arc.RadiusY,
            arc.StartAngleRadians,
            arc.SweepAngleRadians,
            style));
        var endPoint = GetEllipsePoint(arc.Center, arc.RadiusX, arc.RadiusY, arc.StartAngleRadians + arc.SweepAngleRadians);
        items.Add(new CadTransientLine(arc.Center, endPoint, auxiliaryStyle));
        AddMeasurementPreview(
            items,
            arc.Center,
            endPoint,
            $"A {measurementBuilder.FormatAngleDegrees(Math.Abs(arc.SweepAngleRadians))}",
            auxiliaryStyle);
    }

    private void AddEllipseRadiusMeasurements(
        List<CadTransientItem> items,
        EllipseDrawingGeometry geometry,
        CadTransientStyle style)
    {
        AddMeasurementPreview(
            items,
            geometry.Center,
            new CadPointD(geometry.Center.X + geometry.RadiusX, geometry.Center.Y),
            $"X {measurementBuilder.FormatLength(geometry.RadiusX)}",
            style);
        AddMeasurementPreview(
            items,
            geometry.Center,
            new CadPointD(geometry.Center.X, geometry.Center.Y + geometry.RadiusY),
            $"Y {measurementBuilder.FormatLength(geometry.RadiusY)}",
            style);
    }

    private void AddMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        measurementBuilder.AddText(items, lineStart, lineEnd, text, style);
    }
}
