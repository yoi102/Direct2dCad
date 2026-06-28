using Direct2dCad.Db.Cad;

namespace Direct2dCad.Rendering.Handles;

public readonly record struct CadHandleStyle(
    CadColor StrokeColor,
    CadColor FillColor,
    double Size = 7.0,
    double StrokeWidth = 1.0,
    CadHandleShape Shape = CadHandleShape.Square,
    bool KeepSizeScreenConstant = true)
{
    public static CadHandleStyle SelectionOutline { get; } = new(
        CadColor.FromArgb(240, 255, 214, 92),
        CadColor.Transparent,
        Size: 0.0,
        StrokeWidth: 2.0);

    public static CadHandleStyle Grip { get; } = new(
        CadColor.FromArgb(255, 255, 255, 255),
        CadColor.FromArgb(255, 42, 130, 255),
        Size: 7.0,
        StrokeWidth: 1.0,
        Shape: CadHandleShape.Square);
}
