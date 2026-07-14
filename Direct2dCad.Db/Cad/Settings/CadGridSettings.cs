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

public sealed record CadGridSpacingPreset(
    Guid Id,
    string Name,
    double SpacingX,
    double SpacingY,
    bool LinkAxes);

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
    public List<CadGridSpacingPreset> SpacingPresets { get; } = [];
    public Guid? MajorSpacingPresetId { get; set; }
    public Guid? MinorSpacingPresetId { get; set; }

    public CadGridSettings()
    {
        foreach (var spacing in DefaultPresetSpacings)
            SpacingPresets.Add(CreatePreset(spacing, spacing));

        MajorSpacingPresetId = FindPreset(10.0, 10.0)?.Id;
        MinorSpacingPresetId = FindPreset(1.0, 1.0)?.Id;
    }

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

    public void ReplaceSpacingPresets(
        IEnumerable<CadGridSpacingPreset> presets,
        Guid? majorPresetId,
        Guid? minorPresetId)
    {
        ArgumentNullException.ThrowIfNull(presets);
        SpacingPresets.Clear();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            var name = preset.Name?.Trim() ?? string.Empty;
            var spacingY = preset.LinkAxes ? preset.SpacingX : preset.SpacingY;
            if (preset.Id == Guid.Empty ||
                !IsSpacingInRange(preset.SpacingX) ||
                !IsSpacingInRange(spacingY) ||
                SpacingPresets.Any(existing => existing.Id == preset.Id) ||
                (!string.IsNullOrEmpty(name) && !names.Add(name)))
            {
                continue;
            }

            SpacingPresets.Add(preset with { Name = name, SpacingY = spacingY });
        }

        if (SpacingPresets.Count == 0)
        {
            foreach (var spacing in DefaultPresetSpacings)
                SpacingPresets.Add(CreatePreset(spacing, spacing));
        }

        MajorSpacingPresetId = SpacingPresets.Any(item => item.Id == majorPresetId)
            ? majorPresetId
            : null;
        MinorSpacingPresetId = SpacingPresets.Any(item => item.Id == minorPresetId)
            ? minorPresetId
            : null;
        EnsurePresetSelections();
    }

    public void EnsurePresetSelections()
    {
        var major = SpacingPresets.FirstOrDefault(item => item.Id == MajorSpacingPresetId)
                    ?? FindPreset(SpacingX, SpacingY)
                    ?? AddPresetForSpacing(SpacingX, SpacingY);
        var minorX = GetMinorSpacingX();
        var minorY = GetMinorSpacingY();
        var minor = SpacingPresets.FirstOrDefault(item => item.Id == MinorSpacingPresetId)
                    ?? FindPreset(minorX, minorY)
                    ?? AddPresetForSpacing(minorX, minorY);
        MajorSpacingPresetId = major.Id;
        MinorSpacingPresetId = minor.Id;
    }

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

    private CadGridSpacingPreset AddPresetForSpacing(double spacingX, double spacingY)
    {
        var preset = CreatePreset(
            GuardPresetSpacing(spacingX),
            GuardPresetSpacing(spacingY));
        SpacingPresets.Add(preset);
        return preset;
    }

    private CadGridSpacingPreset? FindPreset(double spacingX, double spacingY)
    {
        return SpacingPresets.FirstOrDefault(preset =>
            NearlyEqual(preset.SpacingX, spacingX) &&
            NearlyEqual(preset.SpacingY, spacingY));
    }

    private static CadGridSpacingPreset CreatePreset(double spacingX, double spacingY) =>
        new(Guid.NewGuid(), string.Empty, spacingX, spacingY, NearlyEqual(spacingX, spacingY));

    private static bool IsSpacingInRange(double value) =>
        value >= MinimumSpacingMillimeters &&
        value <= MaximumSpacingMillimeters &&
        double.IsFinite(value);

    private static double GuardPresetSpacing(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinimumSpacingMillimeters, MaximumSpacingMillimeters)
            : 1.0;

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;

    private static readonly double[] DefaultPresetSpacings =
    [
        1_000.0, 500.0, 100.0, 50.0, 25.0, 10.0, 5.0, 2.5,
        1.0, 0.5, 0.25, 0.1, 0.05, 0.025, 0.01, 0.005, 0.001
    ];

}
