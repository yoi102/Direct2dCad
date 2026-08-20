using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Interactions;

public static class CadRadialMenuActionMapper
{
    public static bool TryGetToolMode(
        CadRadialMenuAction action,
        out CadCanvasToolMode toolMode)
    {
        toolMode = action switch
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
            CadRadialMenuAction.Text => CadCanvasToolMode.Text,
            CadRadialMenuAction.SetOrigin => CadCanvasToolMode.SetOrigin,
            _ => default
        };

        return action is >= CadRadialMenuAction.Select and <= CadRadialMenuAction.SetOrigin;
    }
}
