using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentGridSettingsViewModel : DocumentSettingsSectionViewModel
{
    public DocumentGridSettingsViewModel(CadGridSettings settings)
        : base(Strings.GridAndSnapping)
    {
        Load(settings);
    }

    private void Load(CadGridSettings settings)
    {
        GridType = (ViewModelCadGridType)settings.Type;
        GridSpacingX = settings.SpacingX;
        GridSpacingY = settings.SpacingY;
        GridSubdivision = settings.Subdivision;
        GridSnapSpacingX = settings.SnapSpacingX;
        GridSnapSpacingY = settings.SnapSpacingY;
        GridMinimumScreenSpacing = settings.MinimumScreenSpacing;
        GridMinimumWorldSpacing = settings.MinimumWorldSpacing;
        GridMinorLineColor = settings.MinorLineColor;
        GridMajorLineColor = settings.MajorLineColor;
        GridMinorLineWidth = settings.MinorLineWidth;
        GridMajorLineWidth = settings.MajorLineWidth;
        SnapMarkerType = (ViewModelCadSnapMarkerType)settings.SnapMarkerType;
        SnapMarkerColor = settings.SnapMarkerColor;
        SnapMarkerLength = settings.SnapMarkerLength;
        SnapMarkerStrokeWidth = settings.SnapMarkerStrokeWidth;
    }

    [ObservableProperty] public partial ViewModelCadGridType GridType { get; set; }
    [ObservableProperty] public partial double GridSpacingX { get; set; }
    [ObservableProperty] public partial double GridSpacingY { get; set; }
    [ObservableProperty] public partial int GridSubdivision { get; set; }
    [ObservableProperty] public partial double GridSnapSpacingX { get; set; }
    [ObservableProperty] public partial double GridSnapSpacingY { get; set; }
    [ObservableProperty] public partial double GridMinimumScreenSpacing { get; set; }
    [ObservableProperty] public partial double GridMinimumWorldSpacing { get; set; }
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
        if (!IsPositiveFinite(GridSpacingX) || !IsPositiveFinite(GridSpacingY) ||
            GridSubdivision < 1 ||
            !IsNonNegativeFinite(GridSnapSpacingX) || !IsNonNegativeFinite(GridSnapSpacingY) ||
            !IsPositiveFinite(GridMinimumScreenSpacing) || !IsPositiveFinite(GridMinimumWorldSpacing) ||
            !IsPositiveFinite(GridMinorLineWidth) || !IsPositiveFinite(GridMajorLineWidth) ||
            !IsPositiveFinite(SnapMarkerLength) || !IsPositiveFinite(SnapMarkerStrokeWidth))
        {
            return false;
        }

        var grid = settings.Grid;
        grid.Type = (CadGridType)GridType;
        grid.SpacingX = GridSpacingX;
        grid.SpacingY = GridSpacingY;
        grid.Subdivision = GridSubdivision;
        grid.SnapSpacingX = GridSnapSpacingX;
        grid.SnapSpacingY = GridSnapSpacingY;
        grid.MinimumScreenSpacing = GridMinimumScreenSpacing;
        grid.MinimumWorldSpacing = GridMinimumWorldSpacing;
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
}
