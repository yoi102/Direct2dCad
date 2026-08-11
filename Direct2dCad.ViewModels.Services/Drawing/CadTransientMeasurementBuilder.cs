using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal readonly struct CadTransientMeasurementBuilder(
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

        AddText(items, lineStart, lineEnd, FormatLengthLabel(value), style);
    }

    public void AddSegmentMeasurements(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        CadTransientStyle style,
        bool includeAngle = true,
        string lengthPrefix = "L",
        string anglePrefix = "A")
    {
        var length = lineStart.DistanceTo(lineEnd);
        if (length <= double.Epsilon || !IsFinite(length))
            return;

        AddText(
            items,
            lineStart,
            lineEnd,
            $"{lengthPrefix} {FormatLengthLabel(length)}",
            style,
            stackIndex: 0);

        if (includeAngle)
        {
            AddText(
                items,
                lineStart,
                lineEnd,
                $"{anglePrefix} {FormatDirectionLabel(lineStart, lineEnd)}",
                style,
                stackIndex: 1);
        }
    }

    public void AddText(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style,
        int stackIndex = 0)
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
        var position = midpoint + normal * padding * (stackIndex + 1) + unit * padding;
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
        if (!IsFinite(value))
            return "0";

        if (Math.Abs(value) <= double.Epsilon)
            value = 0;

        var precision = Math.Clamp(document.DocumentSettings.LengthPrecision, 0, 12);
        return value.ToString($"F{precision}");
    }

    public string FormatLengthLabel(double value)
    {
        var suffix = document.DocumentSettings.Unit switch
        {
            CadUnit.Millimeter => " mm",
            CadUnit.Centimeter => " cm",
            CadUnit.Meter => " m",
            CadUnit.Inch => " in",
            CadUnit.Foot => " ft",
            CadUnit.Mil => " mil",
            _ => string.Empty
        };

        return FormatLength(value) + suffix;
    }

    public string FormatAngleDegrees(double radians)
    {
        if (!IsFinite(radians))
            return "0";

        var precision = Math.Clamp(document.DocumentSettings.AnglePrecision, 0, 12);
        return CadArc.RadiansToDegrees(radians).ToString($"F{precision}");
    }

    public string FormatDirectionLabel(CadPointD start, CadPointD end)
    {
        var direction = end - start;
        if (direction == CadVectorD.Zero)
            return $"{FormatAngleDegrees(0)}°";

        var angle = Math.Atan2(direction.Y, direction.X);
        if (angle < 0)
            angle += Math.PI * 2.0;

        return $"{FormatAngleDegrees(angle)}°";
    }

    public string FormatAngleLabel(double radians)
    {
        return $"{FormatAngleDegrees(radians)}°";
    }

    private static double EstimateLabelWidth(string text, double height)
    {
        return Math.Max(height * 2.0, text.Length * height * 0.85);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
