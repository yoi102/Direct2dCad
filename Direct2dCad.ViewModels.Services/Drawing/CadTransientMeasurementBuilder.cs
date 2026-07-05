using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Drawing;

internal sealed class CadTransientMeasurementBuilder(
    CadDocument document,
    CadViewport viewport)
{
    public void AddLength(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        double value,
        CadTransientStyle style)
    {
        if (value <= double.Epsilon)
            return;

        AddText(items, lineStart, lineEnd, FormatLength(value), style);
    }

    public void AddText(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var textHeight = 13.0 / zoom;
        var padding = 8.0 / zoom;
        var direction = lineEnd - lineStart;
        var unit = direction.Normalize();
        if (unit == CadVectorD.Zero)
            unit = CadVectorD.UnitX;

        var normal = unit.Perpendicular();
        var midpoint = lineStart + direction * 0.5;
        var position = midpoint + normal * padding + unit * padding;
        var width = EstimateLabelWidth(text, textHeight);
        var boundsHeight = textHeight * 1.35;
        var bounds = CadRectD.FromLTRB(
            position.X,
            position.Y,
            position.X + width,
            position.Y + boundsHeight);

        items.Add(new CadTransientText(text, position, textHeight, bounds, style));
    }

    public string FormatLength(double value)
    {
        var precision = Math.Clamp(document.DocumentSettings.LengthPrecision, 0, 12);
        return value.ToString($"F{precision}");
    }

    public string FormatAngleDegrees(double radians)
    {
        var precision = Math.Clamp(document.DocumentSettings.AnglePrecision, 0, 12);
        return CadArc.RadiansToDegrees(radians).ToString($"F{precision}");
    }

    private static double EstimateLabelWidth(string text, double height)
    {
        return Math.Max(height * 2.0, text.Length * height * 0.85);
    }
}
