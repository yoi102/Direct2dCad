using System;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Db.Cad.Settings;

public enum CadGridType
{
    None,
    Dots,
    Lines,
    Cross
}

public enum CadSnapMarkerType
{
    None = 0,
    Cross = 1,
    X = 2,
    Square = 3,
    InfiniteCross = 4
}

public sealed class CadGridSettings
{
    public CadGridType Type { get; set; } = CadGridType.Lines;
    public double SpacingX { get; set; } = 10.0;
    public double SpacingY { get; set; } = 10.0;
    public int Subdivision { get; set; } = 5;
    public double SnapSpacingX { get; set; }
    public double SnapSpacingY { get; set; }
    public double MinimumScreenSpacing { get; set; } = 28.0;
    public double MinimumWorldSpacing { get; set; } = 1.0;
    public CadColor MinorLineColor { get; set; } = CadColor.FromArgb(180, 255, 255, 255);
    public CadColor MajorLineColor { get; set; } = CadColor.FromArgb(230, 255, 255, 255);
    public double MinorLineWidth { get; set; } = 0.22;
    public double MajorLineWidth { get; set; } = 0.36;
    public CadColor SnapMarkerColor { get; set; } = CadColor.FromArgb(240, 255, 214, 92);
    public double SnapMarkerLength { get; set; } = 58.0;
    public double SnapMarkerStrokeWidth { get; set; } = 1.25;
    public CadSnapMarkerType SnapMarkerType { get; set; } = CadSnapMarkerType.Cross;

    public double GetSnapSpacingX()
    {
        return SnapSpacingX > 0 ? GuardSpacing(SnapSpacingX) : GuardMinimumWorldSpacing(MinimumWorldSpacing);
    }

    public double GetSnapSpacingY()
    {
        return SnapSpacingY > 0 ? GuardSpacing(SnapSpacingY) : GuardMinimumWorldSpacing(MinimumWorldSpacing);
    }

    private static double GuardSpacing(double spacing)
    {
        return spacing <= 0 || double.IsNaN(spacing) || double.IsInfinity(spacing)
            ? 10.0
            : spacing;
    }

    private static double GuardMinimumWorldSpacing(double spacing)
    {
        return spacing <= 0 || double.IsNaN(spacing) || double.IsInfinity(spacing)
            ? 1.0
            : spacing;
    }
}
