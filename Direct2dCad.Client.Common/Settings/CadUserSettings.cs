using Direct2dCad.Db.Cad;

namespace Direct2dCad.Client.Common.Settings;

public sealed class CadUserSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public CadRenderingUserSettings Rendering { get; set; } = new();
    public CadInteractionUserSettings Interaction { get; set; } = new();

    public static CadUserSettings CreateDefault() => new();

    public void Normalize()
    {
        Version = CurrentVersion;
        Rendering ??= new CadRenderingUserSettings();
        Interaction ??= new CadInteractionUserSettings();
        Rendering.Normalize();
        Interaction.Normalize();
    }
}

public sealed class CadRenderingUserSettings
{
    public bool IsAntialiasingEnabled { get; set; } = true;
    public bool IsTextAntialiasingEnabled { get; set; } = true;

    internal void Normalize()
    {
    }
}

public sealed class CadInteractionUserSettings
{
    public CadColor SelectedEntityStrokeColor { get; set; } = CadColor.FromArgb(240, 255, 214, 92);
    public double SelectedEntityStrokeWidth { get; set; } = 2.0;

    public CadColor GripStrokeColor { get; set; } = CadColor.White;
    public CadColor GripFillColor { get; set; } = CadColor.FromArgb(255, 42, 130, 255);
    public double GripSize { get; set; } = 7.0;
    public double GripStrokeWidth { get; set; } = 1.0;

    public CadColor GripPreviewStrokeColor { get; set; } = CadColor.FromArgb(245, 255, 214, 92);
    public CadColor GripPreviewFillColor { get; set; } = CadColor.FromArgb(22, 255, 214, 92);
    public double GripPreviewStrokeWidth { get; set; } = 1.4;

    public CadColor SelectionWindowStrokeColor { get; set; } = CadColor.FromArgb(230, 64, 196, 255);
    public CadColor SelectionWindowFillColor { get; set; } = CadColor.FromArgb(32, 64, 196, 255);
    public double SelectionWindowStrokeWidth { get; set; } = 1.0;

    public CadColor SelectionCrossingStrokeColor { get; set; } = CadColor.FromArgb(230, 92, 220, 128);
    public CadColor SelectionCrossingFillColor { get; set; } = CadColor.FromArgb(36, 92, 220, 128);
    public double SelectionCrossingStrokeWidth { get; set; } = 1.0;

    internal void Normalize()
    {
        SelectedEntityStrokeWidth = GuardPositive(SelectedEntityStrokeWidth, 2.0);
        GripSize = GuardPositive(GripSize, 7.0);
        GripStrokeWidth = GuardPositive(GripStrokeWidth, 1.0);
        GripPreviewStrokeWidth = GuardPositive(GripPreviewStrokeWidth, 1.4);
        SelectionWindowStrokeWidth = GuardPositive(SelectionWindowStrokeWidth, 1.0);
        SelectionCrossingStrokeWidth = GuardPositive(SelectionCrossingStrokeWidth, 1.0);
    }

    private static double GuardPositive(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : fallback;
    }
}
