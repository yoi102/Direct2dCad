using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentSettingsViewModel : ObservableObject, IDocumentSettingsDialogViewModel
{
    private readonly EditorTabViewModel _editorTab;

    public DocumentSettingsViewModel(EditorTabViewModel editorTab, IDialogService dialogService)
    {
        _editorTab = editorTab ?? throw new ArgumentNullException(nameof(editorTab));
        var settings = editorTab.CadDocumentViewModel.CadEditor.Document.ViewSettings;

        Display = new DocumentDisplaySettingsViewModel(settings);
        GridAndSnapping = new DocumentGridSettingsViewModel(
            settings.Grid,
            dialogService,
            editorTab.CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit);
        Origin = new DocumentOriginSettingsViewModel(
            settings.Origin,
            editorTab.CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit);
        Sections = [Display, GridAndSnapping, Origin];
        SelectedSection = Sections[0];
    }

    public DocumentDisplaySettingsViewModel Display { get; }

    public DocumentGridSettingsViewModel GridAndSnapping { get; }

    public DocumentOriginSettingsViewModel Origin { get; }

    public IReadOnlyList<DocumentSettingsSectionViewModel> Sections { get; }

    [ObservableProperty]
    public partial DocumentSettingsSectionViewModel SelectedSection { get; set; }

    [ObservableProperty]
    public partial string? ValidationError { get; private set; }

    public bool TryApply()
    {
        var settings = new CadViewSettings();
        foreach (var section in Sections)
        {
            if (section.TryApplyTo(settings))
                continue;

            SelectedSection = section;
            ValidationError = Direct2dCad.Lang.Strings.Strings.DocumentSettingsInvalidValues;
            return false;
        }

        ValidationError = null;
        if (SettingsEqual(settings, _editorTab.CadDocumentViewModel.CadEditor.Document.ViewSettings))
            return true;

        _editorTab.ApplyDocumentViewSettings(settings);
        return true;
    }

    public void ResetToDefaults()
    {
        foreach (var section in Sections)
            section.ResetToDefaults();

        ValidationError = null;
    }

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
               leftGrid.MinorSpacingX == rightGrid.MinorSpacingX &&
               leftGrid.MinorSpacingY == rightGrid.MinorSpacingY &&
               leftGrid.SpacingPresets.SequenceEqual(rightGrid.SpacingPresets) &&
               leftGrid.MajorSpacingPresetId == rightGrid.MajorSpacingPresetId &&
               leftGrid.MinorSpacingPresetId == rightGrid.MinorSpacingPresetId &&
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
