using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.ViewServices;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentSettingsViewModel : ObservableObject, IDocumentSettingsDialogViewModel
{
    private readonly EditorTabViewModel _editorTab;

    public DocumentSettingsViewModel(EditorTabViewModel editorTab)
    {
        _editorTab = editorTab ?? throw new ArgumentNullException(nameof(editorTab));
        Load(editorTab.CadDocumentViewModel.CadEditor.Document.ViewSettings);
    }

    [ObservableProperty] public partial CadColor BackgroundColor { get; set; }
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
    [ObservableProperty] public partial ViewModelCadOriginDisplayType OriginDisplayType { get; set; }
    [ObservableProperty] public partial ViewModelCadOriginMarkerType OriginMarkerType { get; set; }
    [ObservableProperty] public partial ViewModelCadOriginLinePattern OriginLinePattern { get; set; }
    [ObservableProperty] public partial CadColor OriginColor { get; set; }
    [ObservableProperty] public partial double OriginX { get; set; }
    [ObservableProperty] public partial double OriginY { get; set; }
    [ObservableProperty] public partial double OriginSize { get; set; }
    [ObservableProperty] public partial double OriginStrokeWidth { get; set; }
    [ObservableProperty] public partial string? ValidationError { get; private set; }

    public bool TryApply()
    {
        if (!TryCreateSettings(out var settings))
        {
            ValidationError = Direct2dCad.Lang.Strings.Strings.DocumentSettingsInvalidValues;
            return false;
        }

        ValidationError = null;
        if (SettingsEqual(settings, _editorTab.CadDocumentViewModel.CadEditor.Document.ViewSettings))
            return true;

        _editorTab.ApplyDocumentViewSettings(settings);
        return true;
    }

    private void Load(CadViewSettings settings)
    {
        BackgroundColor = settings.BackgroundColor;

        var grid = settings.Grid;
        GridType = (ViewModelCadGridType)grid.Type;
        GridSpacingX = grid.SpacingX;
        GridSpacingY = grid.SpacingY;
        GridSubdivision = grid.Subdivision;
        GridSnapSpacingX = grid.SnapSpacingX;
        GridSnapSpacingY = grid.SnapSpacingY;
        GridMinimumScreenSpacing = grid.MinimumScreenSpacing;
        GridMinimumWorldSpacing = grid.MinimumWorldSpacing;
        GridMinorLineColor = grid.MinorLineColor;
        GridMajorLineColor = grid.MajorLineColor;
        GridMinorLineWidth = grid.MinorLineWidth;
        GridMajorLineWidth = grid.MajorLineWidth;
        SnapMarkerType = (ViewModelCadSnapMarkerType)grid.SnapMarkerType;
        SnapMarkerColor = grid.SnapMarkerColor;
        SnapMarkerLength = grid.SnapMarkerLength;
        SnapMarkerStrokeWidth = grid.SnapMarkerStrokeWidth;

        var origin = settings.Origin;
        OriginDisplayType = (ViewModelCadOriginDisplayType)origin.DisplayType;
        OriginMarkerType = (ViewModelCadOriginMarkerType)origin.MarkerType;
        OriginLinePattern = (ViewModelCadOriginLinePattern)origin.LinePattern;
        OriginColor = origin.Color;
        OriginX = origin.Position.X;
        OriginY = origin.Position.Y;
        OriginSize = origin.Size;
        OriginStrokeWidth = origin.StrokeWidth;
    }

    private bool TryCreateSettings(out CadViewSettings settings)
    {
        settings = new CadViewSettings();
        if (!IsPositiveFinite(GridSpacingX) || !IsPositiveFinite(GridSpacingY) ||
            GridSubdivision < 1 ||
            !IsNonNegativeFinite(GridSnapSpacingX) || !IsNonNegativeFinite(GridSnapSpacingY) ||
            !IsPositiveFinite(GridMinimumScreenSpacing) || !IsPositiveFinite(GridMinimumWorldSpacing) ||
            !IsPositiveFinite(GridMinorLineWidth) || !IsPositiveFinite(GridMajorLineWidth) ||
            !IsPositiveFinite(SnapMarkerLength) || !IsPositiveFinite(SnapMarkerStrokeWidth) ||
            !IsFinite(OriginX) || !IsFinite(OriginY) ||
            !IsPositiveFinite(OriginSize) || !IsPositiveFinite(OriginStrokeWidth))
        {
            return false;
        }

        settings.BackgroundColor = BackgroundColor;
        settings.Grid.Type = (CadGridType)GridType;
        settings.Grid.SpacingX = GridSpacingX;
        settings.Grid.SpacingY = GridSpacingY;
        settings.Grid.Subdivision = GridSubdivision;
        settings.Grid.SnapSpacingX = GridSnapSpacingX;
        settings.Grid.SnapSpacingY = GridSnapSpacingY;
        settings.Grid.MinimumScreenSpacing = GridMinimumScreenSpacing;
        settings.Grid.MinimumWorldSpacing = GridMinimumWorldSpacing;
        settings.Grid.MinorLineColor = GridMinorLineColor;
        settings.Grid.MajorLineColor = GridMajorLineColor;
        settings.Grid.MinorLineWidth = GridMinorLineWidth;
        settings.Grid.MajorLineWidth = GridMajorLineWidth;
        settings.Grid.SnapMarkerType = (CadSnapMarkerType)SnapMarkerType;
        settings.Grid.SnapMarkerColor = SnapMarkerColor;
        settings.Grid.SnapMarkerLength = SnapMarkerLength;
        settings.Grid.SnapMarkerStrokeWidth = SnapMarkerStrokeWidth;
        settings.Origin.DisplayType = (CadOriginDisplayType)OriginDisplayType;
        settings.Origin.MarkerType = (CadOriginMarkerType)OriginMarkerType;
        settings.Origin.LinePattern = (CadOriginLinePattern)OriginLinePattern;
        settings.Origin.Color = OriginColor;
        settings.Origin.Position = new CadPointD(OriginX, OriginY);
        settings.Origin.Size = OriginSize;
        settings.Origin.StrokeWidth = OriginStrokeWidth;
        return true;
    }

    private static bool IsPositiveFinite(double value) => value > 0 && IsFinite(value);
    private static bool IsNonNegativeFinite(double value) => value >= 0 && IsFinite(value);
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool SettingsEqual(CadViewSettings left, CadViewSettings right)
    {
        var leftGrid = left.Grid;
        var rightGrid = right.Grid;
        var leftOrigin = left.Origin;
        var rightOrigin = right.Origin;

        return left.BackgroundColor == right.BackgroundColor &&
               leftGrid.Type == rightGrid.Type &&
               leftGrid.SpacingX == rightGrid.SpacingX &&
               leftGrid.SpacingY == rightGrid.SpacingY &&
               leftGrid.Subdivision == rightGrid.Subdivision &&
               leftGrid.SnapSpacingX == rightGrid.SnapSpacingX &&
               leftGrid.SnapSpacingY == rightGrid.SnapSpacingY &&
               leftGrid.MinimumScreenSpacing == rightGrid.MinimumScreenSpacing &&
               leftGrid.MinimumWorldSpacing == rightGrid.MinimumWorldSpacing &&
               leftGrid.MinorLineColor == rightGrid.MinorLineColor &&
               leftGrid.MajorLineColor == rightGrid.MajorLineColor &&
               leftGrid.MinorLineWidth == rightGrid.MinorLineWidth &&
               leftGrid.MajorLineWidth == rightGrid.MajorLineWidth &&
               leftGrid.SnapMarkerColor == rightGrid.SnapMarkerColor &&
               leftGrid.SnapMarkerLength == rightGrid.SnapMarkerLength &&
               leftGrid.SnapMarkerStrokeWidth == rightGrid.SnapMarkerStrokeWidth &&
               leftGrid.SnapMarkerType == rightGrid.SnapMarkerType &&
               leftOrigin.Position == rightOrigin.Position &&
               leftOrigin.DisplayType == rightOrigin.DisplayType &&
               leftOrigin.MarkerType == rightOrigin.MarkerType &&
               leftOrigin.LinePattern == rightOrigin.LinePattern &&
               leftOrigin.Color == rightOrigin.Color &&
               leftOrigin.Size == rightOrigin.Size &&
               leftOrigin.StrokeWidth == rightOrigin.StrokeWidth;
    }
}
