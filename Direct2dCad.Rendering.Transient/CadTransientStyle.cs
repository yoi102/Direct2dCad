using Direct2dCad.Db.Cad;

namespace Direct2dCad.Rendering.Transient;

public readonly record struct CadTransientStyle(
    CadColor StrokeColor,
    double StrokeWidth = 1.0,
    CadTransientLinePattern LinePattern = CadTransientLinePattern.Solid,
    CadColor? FillColor = null,
    bool KeepStrokeWidthScreenConstant = true,
    double MinimumScreenStrokeWidth = 0.5)
{
    public static CadTransientStyle Construction { get; } = new(
        CadColor.FromArgb(230, 64, 196, 255),
        1.0,
        CadTransientLinePattern.Dash);

    public static CadTransientStyle SelectionWindow { get; } = new(
        CadColor.FromArgb(230, 64, 196, 255),
        1.0,
        CadTransientLinePattern.Dash,
        CadColor.FromArgb(32, 64, 196, 255));

    public static CadTransientStyle SelectionCrossing { get; } = new(
        CadColor.FromArgb(230, 92, 220, 128),
        1.0,
        CadTransientLinePattern.Dash,
        CadColor.FromArgb(36, 92, 220, 128));

    public static CadTransientStyle PastePreview { get; } = new(
        CadColor.FromArgb(210, 116, 239, 164),
        1.25,
        CadTransientLinePattern.Dash,
        CadColor.FromArgb(24, 116, 239, 164));
}
