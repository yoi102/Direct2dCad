using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Lang;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class CommonEntityPropertyViewModel : IFillPropertySectionViewModel
{
    public bool IsRegion => TryGetEntity(out var entity) && entity is CadRegion;
    public string RegionSummary => TryGetEntity(out var entity) && entity is CadRegion r
        ? $"{CadUiText.Get("RegionContours")}: {r.Contours.Count} · {CadUiText.Get("MeasurementArea")}: {r.Area / Math.Pow(ToModelLength(1), 2):G10} {CadUnitConversion.GetSymbol(DocumentUnit)}²" : "";
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];
    public bool FillControlsEnabled => IsRegion && IsEditable;
    public bool FillColorControlsEnabled => FillControlsEnabled && CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);
    [ObservableProperty] public partial FillStyleOption? SelectedFillStyleOption { get; set; }
    [ObservableProperty] public partial CadColor FillColor { get; set; }

    private void RefreshRegion(CadEntity entity)
    {
        if (entity is CadRegion r)
        {
            FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
            SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, r.FillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, r.FillStyleId);
        }
        foreach (var name in new[] { nameof(IsRegion), nameof(RegionSummary), nameof(FillStyleOptions), nameof(FillControlsEnabled), nameof(FillColorControlsEnabled) }) OnPropertyChanged(name);
    }
    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    { OnPropertyChanged(nameof(FillColorControlsEnabled)); CommitRegionFill(); }
    partial void OnFillColorChanged(CadColor value)
    { if (FillColorControlsEnabled) CommitRegionFill(); }
    private void CommitRegionFill()
    {
        if (_isRefreshing || !FillControlsEnabled || !TryGetEntity(out var entity) || entity is not CadRegion r) return;
        var id = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, SelectedFillStyleOption, FillColor);
        if (r.FillStyleId != id) _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, id);
    }
}
