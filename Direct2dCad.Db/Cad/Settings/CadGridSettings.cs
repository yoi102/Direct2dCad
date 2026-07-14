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
    public const double MinimumSpacingMillimeters = 0.001;
    public const double MaximumSpacingMillimeters = 100_000.0;
    public const int MinimumSubdivision = 2;
    public const int MaximumSubdivision = 100;

    public CadGridType Type { get; set; } = CadGridType.Lines;
    public double SpacingX { get; set; } = 10.0;
    public double SpacingY { get; set; } = 10.0;
    public double MinorSpacingX { get; set; } = 1.0;
    public double MinorSpacingY { get; set; } = 1.0;
    public int Subdivision { get; set; } = 10;
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
        return SnapSpacingX > 0 ? GuardSpacing(SnapSpacingX) : GetMinorSpacingX();
    }

    public double GetSnapSpacingY()
    {
        return SnapSpacingY > 0 ? GuardSpacing(SnapSpacingY) : GetMinorSpacingY();
    }

    public double GetMinorSpacingX() => GuardMinorSpacing(MinorSpacingX, SpacingX);

    public double GetMinorSpacingY() => GuardMinorSpacing(MinorSpacingY, SpacingY);

    private static double GuardSpacing(double spacing)
    {
        return spacing <= 0 || double.IsNaN(spacing) || double.IsInfinity(spacing)
            ? 10.0
            : spacing;
    }

    private double GuardMinorSpacing(double spacing, double majorSpacing)
    {
        if (spacing >= MinimumSpacingMillimeters &&
            spacing <= MaximumSpacingMillimeters &&
            double.IsFinite(spacing))
        {
            return spacing;
        }

        var subdivision = Math.Clamp(Subdivision, MinimumSubdivision, MaximumSubdivision);
        return Math.Clamp(
            GuardSpacing(majorSpacing) / subdivision,
            MinimumSpacingMillimeters,
            MaximumSpacingMillimeters);
    }

}
