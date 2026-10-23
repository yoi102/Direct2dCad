using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Interactions;

public static class CadRadialMenuActionMapper
{
    public static bool TryGetToolMode(
        CadRadialMenuAction action,
        out CadCanvasToolMode toolMode)
    {
        CadCanvasToolMode? mappedMode = action switch
        {
            CadRadialMenuAction.Select => CadCanvasToolMode.Select,
            CadRadialMenuAction.Line => CadCanvasToolMode.Line,
            CadRadialMenuAction.CircleCenterRadius => CadCanvasToolMode.CircleCenterRadius,
            CadRadialMenuAction.CircleCenterDiameter => CadCanvasToolMode.CircleCenterDiameter,
            CadRadialMenuAction.CircleTwoPoint => CadCanvasToolMode.CircleTwoPoint,
            CadRadialMenuAction.CircleThreePoint => CadCanvasToolMode.CircleThreePoint,
            CadRadialMenuAction.EllipseCenter => CadCanvasToolMode.EllipseCenter,
            CadRadialMenuAction.EllipseAxisEnd => CadCanvasToolMode.EllipseAxisEnd,
            CadRadialMenuAction.EllipseArc => CadCanvasToolMode.EllipseArc,
            CadRadialMenuAction.ArcThreePoint => CadCanvasToolMode.ArcThreePoint,
            CadRadialMenuAction.ArcStartCenterEnd => CadCanvasToolMode.ArcStartCenterEnd,
            CadRadialMenuAction.ArcStartCenterAngle => CadCanvasToolMode.ArcStartCenterAngle,
            CadRadialMenuAction.ArcStartCenterLength => CadCanvasToolMode.ArcStartCenterLength,
            CadRadialMenuAction.ArcStartEndAngle => CadCanvasToolMode.ArcStartEndAngle,
            CadRadialMenuAction.ArcStartEndDirection => CadCanvasToolMode.ArcStartEndDirection,
            CadRadialMenuAction.ArcStartEndRadius => CadCanvasToolMode.ArcStartEndRadius,
            CadRadialMenuAction.ArcCenterStartEnd => CadCanvasToolMode.ArcCenterStartEnd,
            CadRadialMenuAction.ArcCenterStartAngle => CadCanvasToolMode.ArcCenterStartAngle,
            CadRadialMenuAction.ArcCenterStartLength => CadCanvasToolMode.ArcCenterStartLength,
            CadRadialMenuAction.ArcContinue => CadCanvasToolMode.ArcContinue,
            CadRadialMenuAction.Rectangle => CadCanvasToolMode.Rectangle,
            CadRadialMenuAction.Polyline => CadCanvasToolMode.Polyline,
            CadRadialMenuAction.Polygon => CadCanvasToolMode.Polygon,
            CadRadialMenuAction.Spline => CadCanvasToolMode.Spline,
            CadRadialMenuAction.SplineCv => CadCanvasToolMode.SplineCv,
            CadRadialMenuAction.Offset => CadCanvasToolMode.Offset,
            CadRadialMenuAction.Trim => CadCanvasToolMode.Trim,
            CadRadialMenuAction.Extend => CadCanvasToolMode.Extend,
            CadRadialMenuAction.Fillet => CadCanvasToolMode.Fillet,
            CadRadialMenuAction.Chamfer => CadCanvasToolMode.Chamfer,
            CadRadialMenuAction.Join => CadCanvasToolMode.Join,
            CadRadialMenuAction.Break => CadCanvasToolMode.Break,
            CadRadialMenuAction.RectArray => CadCanvasToolMode.RectArray,
            CadRadialMenuAction.PolarArray => CadCanvasToolMode.PolarArray,
            CadRadialMenuAction.DimLinearX => CadCanvasToolMode.DimLinearX,
            CadRadialMenuAction.DimLinearY => CadCanvasToolMode.DimLinearY,
            CadRadialMenuAction.DimAligned => CadCanvasToolMode.DimAligned,
            CadRadialMenuAction.DimRadius => CadCanvasToolMode.DimRadius,
            CadRadialMenuAction.DimDiameter => CadCanvasToolMode.DimDiameter,
            CadRadialMenuAction.DimAngular => CadCanvasToolMode.DimAngular,
            CadRadialMenuAction.Leader => CadCanvasToolMode.Leader,
            CadRadialMenuAction.Text => CadCanvasToolMode.Text,
            CadRadialMenuAction.SetOrigin => CadCanvasToolMode.SetOrigin,
            _ => null
        };

        toolMode = mappedMode.GetValueOrDefault();
        return mappedMode.HasValue;
    }
}
