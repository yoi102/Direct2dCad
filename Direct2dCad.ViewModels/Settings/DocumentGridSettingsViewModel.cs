using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentGridSettingsViewModel : DocumentSettingsSectionViewModel
{
    private bool _isLoadingDensity;

    public DocumentGridSettingsViewModel(CadGridSettings settings)
        : base(Strings.GridAndSnapping)
    {
        GridDensityPresets =
        [
            new(0.01, 0.001),
            new(0.05, 0.01),
            new(0.1, 0.01),
            new(0.25, 0.05),
            new(0.5, 0.1),
            new(1.0, 0.1),
            new(2.5, 0.5),
            new(5.0, 1.0),
            new(10.0, 1.0),
            new(25.0, 5.0),
            new(50.0, 10.0),
            new(100.0, 10.0),
            new(500.0, 100.0),
            new(1_000.0, 100.0)
        ];
        Load(settings);
    }

    public IReadOnlyList<GridDensityPresetViewModel> GridDensityPresets { get; }

    [ObservableProperty] public partial ViewModelCadGridType GridType { get; set; }
    [ObservableProperty] public partial GridDensityPresetViewModel? SelectedGridDensityPreset { get; set; }
    [ObservableProperty] public partial double GridMajorSpacingXMillimeters { get; set; }
    [ObservableProperty] public partial double GridMajorSpacingYMillimeters { get; set; }
    [ObservableProperty] public partial double GridMinorSpacingXMillimeters { get; set; }
    [ObservableProperty] public partial double GridMinorSpacingYMillimeters { get; set; }
    [ObservableProperty] public partial double GridMinimumScreenSpacing { get; set; }
    [ObservableProperty] public partial CadColor GridMinorLineColor { get; set; }
    [ObservableProperty] public partial CadColor GridMajorLineColor { get; set; }
    [ObservableProperty] public partial double GridMinorLineWidth { get; set; }
    [ObservableProperty] public partial double GridMajorLineWidth { get; set; }
    [ObservableProperty] public partial ViewModelCadSnapMarkerType SnapMarkerType { get; set; }
    [ObservableProperty] public partial CadColor SnapMarkerColor { get; set; }
    [ObservableProperty] public partial double SnapMarkerLength { get; set; }
    [ObservableProperty] public partial double SnapMarkerStrokeWidth { get; set; }

    internal override bool TryApplyTo(CadViewSettings settings)
    {
        if (!TryResolveSubdivision(GridMajorSpacingXMillimeters, GridMinorSpacingXMillimeters, out var subdivisionX) ||
            !TryResolveSubdivision(GridMajorSpacingYMillimeters, GridMinorSpacingYMillimeters, out var subdivisionY) ||
            !IsPositiveFinite(GridMinimumScreenSpacing) ||
            !IsPositiveFinite(GridMinorLineWidth) || !IsPositiveFinite(GridMajorLineWidth) ||
            !IsPositiveFinite(SnapMarkerLength) || !IsPositiveFinite(SnapMarkerStrokeWidth))
        {
            return false;
        }

        var grid = settings.Grid;
        grid.Type = (CadGridType)GridType;
        grid.SpacingX = GridMajorSpacingXMillimeters;
        grid.SpacingY = GridMajorSpacingYMillimeters;
        grid.MinorSpacingX = GridMinorSpacingXMillimeters;
        grid.MinorSpacingY = GridMinorSpacingYMillimeters;
        grid.Subdivision = Math.Max(subdivisionX, subdivisionY);
        grid.SnapSpacingX = 0;
        grid.SnapSpacingY = 0;
        grid.MinimumScreenSpacing = GridMinimumScreenSpacing;
        grid.MinimumWorldSpacing = Math.Min(GridMinorSpacingXMillimeters, GridMinorSpacingYMillimeters);
        grid.MinorLineColor = GridMinorLineColor;
        grid.MajorLineColor = GridMajorLineColor;
        grid.MinorLineWidth = GridMinorLineWidth;
        grid.MajorLineWidth = GridMajorLineWidth;
        grid.SnapMarkerType = (CadSnapMarkerType)SnapMarkerType;
        grid.SnapMarkerColor = SnapMarkerColor;
        grid.SnapMarkerLength = SnapMarkerLength;
        grid.SnapMarkerStrokeWidth = SnapMarkerStrokeWidth;
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadGridSettings());
    }

    partial void OnSelectedGridDensityPresetChanged(GridDensityPresetViewModel? value)
    {
        if (_isLoadingDensity || value is null)
            return;

        _isLoadingDensity = true;
        GridMajorSpacingXMillimeters = value.MajorSpacingMillimeters;
        GridMajorSpacingYMillimeters = value.MajorSpacingMillimeters;
        GridMinorSpacingXMillimeters = value.MinorSpacingMillimeters;
        GridMinorSpacingYMillimeters = value.MinorSpacingMillimeters;
        _isLoadingDensity = false;
    }

    partial void OnGridMajorSpacingXMillimetersChanged(double value) => RefreshSelectedPreset();
    partial void OnGridMajorSpacingYMillimetersChanged(double value) => RefreshSelectedPreset();
    partial void OnGridMinorSpacingXMillimetersChanged(double value) => RefreshSelectedPreset();
    partial void OnGridMinorSpacingYMillimetersChanged(double value) => RefreshSelectedPreset();

    private void Load(CadGridSettings settings)
    {
        GridType = (ViewModelCadGridType)settings.Type;
        _isLoadingDensity = true;
        GridMajorSpacingXMillimeters = settings.SpacingX;
        GridMajorSpacingYMillimeters = settings.SpacingY;
        GridMinorSpacingXMillimeters = settings.GetMinorSpacingX();
        GridMinorSpacingYMillimeters = settings.GetMinorSpacingY();
        SelectedGridDensityPreset = FindMatchingPreset();
        _isLoadingDensity = false;
        GridMinimumScreenSpacing = settings.MinimumScreenSpacing;
        GridMinorLineColor = settings.MinorLineColor;
        GridMajorLineColor = settings.MajorLineColor;
        GridMinorLineWidth = settings.MinorLineWidth;
        GridMajorLineWidth = settings.MajorLineWidth;
        SnapMarkerType = (ViewModelCadSnapMarkerType)settings.SnapMarkerType;
        SnapMarkerColor = settings.SnapMarkerColor;
        SnapMarkerLength = settings.SnapMarkerLength;
        SnapMarkerStrokeWidth = settings.SnapMarkerStrokeWidth;
    }

    private void RefreshSelectedPreset()
    {
        if (_isLoadingDensity)
            return;

        _isLoadingDensity = true;
        SelectedGridDensityPreset = FindMatchingPreset();
        _isLoadingDensity = false;
    }

    private GridDensityPresetViewModel? FindMatchingPreset()
    {
        if (!NearlyEqual(GridMajorSpacingXMillimeters, GridMajorSpacingYMillimeters) ||
            !NearlyEqual(GridMinorSpacingXMillimeters, GridMinorSpacingYMillimeters))
        {
            return null;
        }

        return GridDensityPresets.FirstOrDefault(preset =>
            NearlyEqual(preset.MajorSpacingMillimeters, GridMajorSpacingXMillimeters) &&
            NearlyEqual(preset.MinorSpacingMillimeters, GridMinorSpacingXMillimeters));
    }

    private static bool TryResolveSubdivision(double major, double minor, out int subdivision)
    {
        subdivision = 0;
        if (major < CadGridSettings.MinimumSpacingMillimeters ||
            major > CadGridSettings.MaximumSpacingMillimeters ||
            minor < CadGridSettings.MinimumSpacingMillimeters ||
            minor > CadGridSettings.MaximumSpacingMillimeters ||
            !double.IsFinite(major) || !double.IsFinite(minor))
        {
            return false;
        }

        var ratio = major / minor;
        var rounded = Math.Round(ratio, MidpointRounding.AwayFromZero);
        if (rounded < CadGridSettings.MinimumSubdivision ||
            rounded > CadGridSettings.MaximumSubdivision ||
            !NearlyEqual(ratio, rounded))
        {
            return false;
        }

        subdivision = (int)rounded;
        return true;
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;
}

public sealed record GridDensityPresetViewModel(
    double MajorSpacingMillimeters,
    double MinorSpacingMillimeters)
{
    public string DisplayName => $"{MajorSpacingMillimeters:0.###} / {MinorSpacingMillimeters:0.###} mm";
}
