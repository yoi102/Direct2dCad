namespace Direct2dCad.Client.Common.Settings;

public enum CadRadialMenuGesture
{
    Middle,
    ShiftMiddle,
    ControlMiddle,
    AltMiddle
}

public enum CadRadialMenuAction
{
    None,
    Select,
    Line,
    CircleCenterRadius,
    CircleCenterDiameter,
    CircleTwoPoint,
    CircleThreePoint,
    EllipseCenter,
    EllipseAxisEnd,
    EllipseArc,
    ArcThreePoint,
    ArcStartCenterEnd,
    ArcStartCenterAngle,
    ArcStartCenterLength,
    ArcStartEndAngle,
    ArcStartEndDirection,
    ArcStartEndRadius,
    ArcCenterStartEnd,
    ArcCenterStartAngle,
    ArcCenterStartLength,
    ArcContinue,
    Rectangle,
    Polyline,
    Polygon,
    Spline,
    Text,
    SetOrigin,
    Undo,
    Redo,
    CopySelection,
    CutSelection,
    Paste,
    DeleteSelection,
    SelectAll,
    ClearSelection,
    CancelCurrentInteraction,
    FitToWindow,
    Save,
    SaveAs,
    CreateBlock
}

public static class CadRadialMenuActionCatalog
{
    public static string GetResourceKey(CadRadialMenuAction action) => action switch
    {
        CadRadialMenuAction.FitToWindow => "Fit",
        _ => action.ToString()
    };
}

/// <summary>
/// User-owned radial-menu configuration. Each gesture exposes eight sectors,
/// beginning at twelve o'clock and continuing clockwise.
/// </summary>
public sealed class CadRadialMenuSettings
{
    public const int SectorCount = 8;

    public bool IsEnabled { get; set; } = true;

    public CadRadialMenuAction[] MiddleActions { get; set; } =
    [
        CadRadialMenuAction.Select, CadRadialMenuAction.Line,
        CadRadialMenuAction.CircleCenterRadius, CadRadialMenuAction.Rectangle,
        CadRadialMenuAction.Polyline, CadRadialMenuAction.ArcThreePoint,
        CadRadialMenuAction.Text, CadRadialMenuAction.EllipseCenter
    ];

    public CadRadialMenuAction[] ShiftMiddleActions { get; set; } =
    [
        CadRadialMenuAction.Undo, CadRadialMenuAction.Redo,
        CadRadialMenuAction.CopySelection, CadRadialMenuAction.Paste,
        CadRadialMenuAction.DeleteSelection, CadRadialMenuAction.SelectAll,
        CadRadialMenuAction.ClearSelection, CadRadialMenuAction.CancelCurrentInteraction
    ];

    public CadRadialMenuAction[] ControlMiddleActions { get; set; } =
    [
        CadRadialMenuAction.CircleCenterDiameter, CadRadialMenuAction.CircleTwoPoint,
        CadRadialMenuAction.CircleThreePoint, CadRadialMenuAction.ArcStartCenterEnd,
        CadRadialMenuAction.ArcStartEndAngle, CadRadialMenuAction.ArcCenterStartEnd,
        CadRadialMenuAction.Polygon, CadRadialMenuAction.Spline
    ];

    public CadRadialMenuAction[] AltMiddleActions { get; set; } =
    [
        CadRadialMenuAction.EllipseAxisEnd, CadRadialMenuAction.EllipseArc,
        CadRadialMenuAction.ArcStartCenterAngle, CadRadialMenuAction.ArcStartEndDirection,
        CadRadialMenuAction.ArcContinue, CadRadialMenuAction.SetOrigin,
        CadRadialMenuAction.FitToWindow, CadRadialMenuAction.Save
    ];

    public IReadOnlyList<CadRadialMenuAction> GetActions(CadRadialMenuGesture gesture) => gesture switch
    {
        CadRadialMenuGesture.ShiftMiddle => ShiftMiddleActions,
        CadRadialMenuGesture.ControlMiddle => ControlMiddleActions,
        CadRadialMenuGesture.AltMiddle => AltMiddleActions,
        _ => MiddleActions
    };

    public void SetActions(CadRadialMenuGesture gesture, IEnumerable<CadRadialMenuAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var normalized = NormalizeActions(actions.ToArray(), GetDefaultActions(gesture));
        switch (gesture)
        {
            case CadRadialMenuGesture.ShiftMiddle:
                ShiftMiddleActions = normalized;
                break;
            case CadRadialMenuGesture.ControlMiddle:
                ControlMiddleActions = normalized;
                break;
            case CadRadialMenuGesture.AltMiddle:
                AltMiddleActions = normalized;
                break;
            default:
                MiddleActions = normalized;
                break;
        }
    }

    public void Normalize()
    {
        MiddleActions = NormalizeActions(MiddleActions, GetDefaultActions(CadRadialMenuGesture.Middle));
        ShiftMiddleActions = NormalizeActions(ShiftMiddleActions, GetDefaultActions(CadRadialMenuGesture.ShiftMiddle));
        ControlMiddleActions = NormalizeActions(ControlMiddleActions, GetDefaultActions(CadRadialMenuGesture.ControlMiddle));
        AltMiddleActions = NormalizeActions(AltMiddleActions, GetDefaultActions(CadRadialMenuGesture.AltMiddle));
    }

    private static CadRadialMenuAction[] NormalizeActions(
        IReadOnlyList<CadRadialMenuAction>? actions,
        IReadOnlyList<CadRadialMenuAction> defaults)
    {
        var normalized = new CadRadialMenuAction[SectorCount];
        for (var index = 0; index < normalized.Length; index++)
        {
            var action = actions is not null && index < actions.Count
                ? actions[index]
                : defaults[index];
            normalized[index] = Enum.IsDefined(action) ? action : defaults[index];
        }

        return normalized;
    }

    private static CadRadialMenuAction[] GetDefaultActions(CadRadialMenuGesture gesture) => gesture switch
    {
        CadRadialMenuGesture.ShiftMiddle =>
        [
            CadRadialMenuAction.Undo, CadRadialMenuAction.Redo,
            CadRadialMenuAction.CopySelection, CadRadialMenuAction.Paste,
            CadRadialMenuAction.DeleteSelection, CadRadialMenuAction.SelectAll,
            CadRadialMenuAction.ClearSelection, CadRadialMenuAction.CancelCurrentInteraction
        ],
        CadRadialMenuGesture.ControlMiddle =>
        [
            CadRadialMenuAction.CircleCenterDiameter, CadRadialMenuAction.CircleTwoPoint,
            CadRadialMenuAction.CircleThreePoint, CadRadialMenuAction.ArcStartCenterEnd,
            CadRadialMenuAction.ArcStartEndAngle, CadRadialMenuAction.ArcCenterStartEnd,
            CadRadialMenuAction.Polygon, CadRadialMenuAction.Spline
        ],
        CadRadialMenuGesture.AltMiddle =>
        [
            CadRadialMenuAction.EllipseAxisEnd, CadRadialMenuAction.EllipseArc,
            CadRadialMenuAction.ArcStartCenterAngle, CadRadialMenuAction.ArcStartEndDirection,
            CadRadialMenuAction.ArcContinue, CadRadialMenuAction.SetOrigin,
            CadRadialMenuAction.FitToWindow, CadRadialMenuAction.Save
        ],
        _ =>
        [
            CadRadialMenuAction.Select, CadRadialMenuAction.Line,
            CadRadialMenuAction.CircleCenterRadius, CadRadialMenuAction.Rectangle,
            CadRadialMenuAction.Polyline, CadRadialMenuAction.ArcThreePoint,
            CadRadialMenuAction.Text, CadRadialMenuAction.EllipseCenter
        ]
    };
}
