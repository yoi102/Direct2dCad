using System.Windows;
using System.Windows.Controls;
using Direct2dCad.Client.Common.Settings;
using MaterialDesignThemes.Wpf;

namespace Direct2dCad.wpf.Controls;

/// <summary>Displays the same glyph used by the editor toolbar for a radial-menu action.</summary>
public sealed class CadRadialMenuActionIcon : ContentControl
{
    public const double DefaultIconSize = 28;

    public CadRadialMenuActionIcon()
    {
        Width = DefaultIconSize;
        Height = DefaultIconSize;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        Loaded += (_, _) => UpdateIcon();
    }

    public CadRadialMenuAction Action
    {
        get => (CadRadialMenuAction)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(
            nameof(Action),
            typeof(CadRadialMenuAction),
            typeof(CadRadialMenuActionIcon),
            new PropertyMetadata(CadRadialMenuAction.None, OnActionChanged));

    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CadRadialMenuActionIcon)d).UpdateIcon();

    private void UpdateIcon()
    {
        if (Action == CadRadialMenuAction.None)
        {
            Content = null;
            ContentTemplate = null;
            return;
        }

        var templateKey = CadRadialMenuActionIconCatalog.GetTemplateKey(Action);
        if (templateKey is not null && TryFindResource(templateKey) is DataTemplate template)
        {
            Content = null;
            ContentTemplate = template;
            return;
        }

        ContentTemplate = null;
        Content = new PackIcon
        {
            Kind = CadRadialMenuActionIconCatalog.GetFallbackKind(Action),
            Width = DefaultIconSize,
            Height = DefaultIconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }
}

internal static class CadRadialMenuActionIconCatalog
{
    public static string? GetTemplateKey(CadRadialMenuAction action) => action switch
    {
        CadRadialMenuAction.Line => "CadLineIconTemplate",
        CadRadialMenuAction.Polyline => "CadPolylineIconTemplate",
        CadRadialMenuAction.Polygon => "CadPolygonIconTemplate",
        CadRadialMenuAction.CircleCenterRadius => "CadCircleCenterRadiusIconTemplate",
        CadRadialMenuAction.CircleCenterDiameter => "CadCircleCenterDiameterIconTemplate",
        CadRadialMenuAction.CircleTwoPoint => "CadCircleTwoPointIconTemplate",
        CadRadialMenuAction.CircleThreePoint => "CadCircleThreePointIconTemplate",
        CadRadialMenuAction.EllipseCenter => "CadEllipseCenterIconTemplate",
        CadRadialMenuAction.EllipseAxisEnd => "CadEllipseAxisEndIconTemplate",
        CadRadialMenuAction.EllipseArc => "CadEllipseArcIconTemplate",
        CadRadialMenuAction.ArcThreePoint => "CadArcThreePointIconTemplate",
        CadRadialMenuAction.ArcStartCenterEnd => "CadArcStartCenterEndIconTemplate",
        CadRadialMenuAction.ArcStartCenterAngle => "CadArcStartCenterAngleIconTemplate",
        CadRadialMenuAction.ArcStartCenterLength => "CadArcStartCenterLengthIconTemplate",
        CadRadialMenuAction.ArcStartEndAngle => "CadArcStartEndAngleIconTemplate",
        CadRadialMenuAction.ArcStartEndDirection => "CadArcStartEndDirectionIconTemplate",
        CadRadialMenuAction.ArcStartEndRadius => "CadArcStartEndRadiusIconTemplate",
        CadRadialMenuAction.ArcCenterStartEnd => "CadArcCenterStartEndIconTemplate",
        CadRadialMenuAction.ArcCenterStartAngle => "CadArcCenterStartAngleIconTemplate",
        CadRadialMenuAction.ArcCenterStartLength => "CadArcCenterStartLengthIconTemplate",
        CadRadialMenuAction.ArcContinue => "CadArcContinueIconTemplate",
        CadRadialMenuAction.Spline => "CadSplineIconTemplate",
        _ => null
    };

    public static PackIconKind GetFallbackKind(CadRadialMenuAction action) => action switch
    {
        CadRadialMenuAction.Select => PackIconKind.ArrowAll,
        CadRadialMenuAction.Line or CadRadialMenuAction.Polyline or CadRadialMenuAction.Spline => PackIconKind.Drawing,
        CadRadialMenuAction.CircleCenterRadius or CadRadialMenuAction.CircleCenterDiameter or
            CadRadialMenuAction.CircleTwoPoint or CadRadialMenuAction.CircleThreePoint or
            CadRadialMenuAction.EllipseCenter or CadRadialMenuAction.EllipseAxisEnd or
            CadRadialMenuAction.EllipseArc => PackIconKind.Circle,
        CadRadialMenuAction.ArcThreePoint or CadRadialMenuAction.ArcStartCenterEnd or
            CadRadialMenuAction.ArcStartCenterAngle or CadRadialMenuAction.ArcStartCenterLength or
            CadRadialMenuAction.ArcStartEndAngle or CadRadialMenuAction.ArcStartEndDirection or
            CadRadialMenuAction.ArcStartEndRadius or CadRadialMenuAction.ArcCenterStartEnd or
            CadRadialMenuAction.ArcCenterStartAngle or CadRadialMenuAction.ArcCenterStartLength or
            CadRadialMenuAction.ArcContinue => PackIconKind.Drawing,
        CadRadialMenuAction.Rectangle => PackIconKind.RectangleOutline,
        CadRadialMenuAction.Polygon or CadRadialMenuAction.CreateBlock => PackIconKind.ShapePlus,
        CadRadialMenuAction.Text => PackIconKind.TextShadow,
        CadRadialMenuAction.SetOrigin => PackIconKind.Target,
        CadRadialMenuAction.Undo => PackIconKind.Undo,
        CadRadialMenuAction.Redo => PackIconKind.Redo,
        CadRadialMenuAction.CopySelection => PackIconKind.ContentCopy,
        CadRadialMenuAction.CutSelection => PackIconKind.ContentCut,
        CadRadialMenuAction.Paste => PackIconKind.ContentPaste,
        CadRadialMenuAction.DeleteSelection => PackIconKind.Delete,
        CadRadialMenuAction.SelectAll => PackIconKind.SelectAll,
        CadRadialMenuAction.ClearSelection or CadRadialMenuAction.CancelCurrentInteraction => PackIconKind.SelectionOff,
        CadRadialMenuAction.FitToWindow => PackIconKind.FitToScreen,
        CadRadialMenuAction.Save => PackIconKind.ContentSave,
        CadRadialMenuAction.SaveAs => PackIconKind.ContentSaveAdd,
        _ => PackIconKind.Cancel
    };
}
