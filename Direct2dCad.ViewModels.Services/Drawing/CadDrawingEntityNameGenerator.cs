using System.Globalization;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Services.Drawing;

public static class CadDrawingEntityNameGenerator
{
    public static string CreateNext(CadDocument document, CadCanvasToolMode toolMode)
    {
        ArgumentNullException.ThrowIfNull(document);

        var prefix = GetPrefix(toolMode);
        if (prefix is null)
            return string.Empty;

        var maximumSuffix = 0;
        foreach (var entity in document.Entities.Values)
        {
            var name = entity.Name;
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = name.AsSpan(prefix.Length);
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                number > maximumSuffix)
            {
                maximumSuffix = number;
            }
        }

        return $"{prefix}{maximumSuffix + 1}";
    }

    private static string? GetPrefix(CadCanvasToolMode toolMode) => toolMode switch
    {
        CadCanvasToolMode.Line => "Line",
        CadCanvasToolMode.CircleCenterRadius or
        CadCanvasToolMode.CircleCenterDiameter or
        CadCanvasToolMode.CircleTwoPoint or
        CadCanvasToolMode.CircleThreePoint => "Circle",
        CadCanvasToolMode.EllipseCenter or
        CadCanvasToolMode.EllipseAxisEnd => "Ellipse",
        CadCanvasToolMode.EllipseArc => "EllipseArc",
        CadCanvasToolMode.ArcThreePoint or
        CadCanvasToolMode.ArcStartCenterEnd or
        CadCanvasToolMode.ArcStartCenterAngle or
        CadCanvasToolMode.ArcStartCenterLength or
        CadCanvasToolMode.ArcStartEndAngle or
        CadCanvasToolMode.ArcStartEndDirection or
        CadCanvasToolMode.ArcStartEndRadius or
        CadCanvasToolMode.ArcCenterStartEnd or
        CadCanvasToolMode.ArcCenterStartAngle or
        CadCanvasToolMode.ArcCenterStartLength or
        CadCanvasToolMode.ArcContinue => "Arc",
        CadCanvasToolMode.Rectangle => "Rectangle",
        CadCanvasToolMode.Polyline => "Polyline",
        CadCanvasToolMode.Polygon => "Polygon",
        CadCanvasToolMode.Spline => "Spline",
        CadCanvasToolMode.Text => "Text",
        _ => null
    };
}
