using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentGridSettingsViewModel : DocumentSettingsSectionViewModel
{
    private readonly IDialogService _dialogService;

    public DocumentGridSettingsViewModel(CadGridSettings settings, IDialogService dialogService)
        : base(Strings.GridAndSnapping)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        Load(settings);
    }

    public ObservableCollection<GridSpacingPresetItemViewModel> GridSpacingPresets { get; } = [];

    [ObservableProperty] public partial ViewModelCadGridType GridType { get; set; }
    [ObservableProperty] public partial GridSpacingPresetItemViewModel? SelectedGridSpacingPreset { get; set; }
    [ObservableProperty] public partial GridSpacingPresetItemViewModel? SelectedMajorGridPreset { get; set; }
    [ObservableProperty] public partial GridSpacingPresetItemViewModel? SelectedMinorGridPreset { get; set; }
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
        if (SelectedMajorGridPreset is null ||
            SelectedMinorGridPreset is null ||
            GridSpacingPresets.Count < 2 ||
            !TryResolveSubdivision(
                SelectedMajorGridPreset.SpacingX,
                SelectedMinorGridPreset.SpacingX,
                out var subdivisionX) ||
            !TryResolveSubdivision(
                SelectedMajorGridPreset.SpacingY,
                SelectedMinorGridPreset.SpacingY,
                out var subdivisionY) ||
            !IsPositiveFinite(GridMinimumScreenSpacing) ||
            !IsPositiveFinite(GridMinorLineWidth) || !IsPositiveFinite(GridMajorLineWidth) ||
            !IsPositiveFinite(SnapMarkerLength) || !IsPositiveFinite(SnapMarkerStrokeWidth))
        {
            return false;
        }

        var grid = settings.Grid;
        grid.Type = (CadGridType)GridType;
        grid.SpacingX = SelectedMajorGridPreset.SpacingX;
        grid.SpacingY = SelectedMajorGridPreset.SpacingY;
        grid.MinorSpacingX = SelectedMinorGridPreset.SpacingX;
        grid.MinorSpacingY = SelectedMinorGridPreset.SpacingY;
        grid.Subdivision = Math.Max(subdivisionX, subdivisionY);
        grid.SnapSpacingX = 0;
        grid.SnapSpacingY = 0;
        grid.MinimumScreenSpacing = GridMinimumScreenSpacing;
        grid.MinimumWorldSpacing = Math.Min(grid.MinorSpacingX, grid.MinorSpacingY);
        grid.MinorLineColor = GridMinorLineColor;
        grid.MajorLineColor = GridMajorLineColor;
        grid.MinorLineWidth = GridMinorLineWidth;
        grid.MajorLineWidth = GridMajorLineWidth;
        grid.SnapMarkerType = (CadSnapMarkerType)SnapMarkerType;
        grid.SnapMarkerColor = SnapMarkerColor;
        grid.SnapMarkerLength = SnapMarkerLength;
        grid.SnapMarkerStrokeWidth = SnapMarkerStrokeWidth;
        grid.ReplaceSpacingPresets(
            GridSpacingPresets.Select(item => item.ToModel()),
            SelectedMajorGridPreset.Id,
            SelectedMinorGridPreset.Id);
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadGridSettings());
    }

    [RelayCommand]
    private async Task AddGridSpacingPresetAsync()
    {
        var result = await _dialogService.ShowGridSpacingPresetDialogAsync(
            new GridSpacingPresetDialogRequest(
                false,
                string.Empty,
                1.0,
                1.0,
                true,
                GetUsedNames()));
        if (result is null)
            return;

        var item = new GridSpacingPresetItemViewModel(
            Guid.NewGuid(),
            result.Name,
            result.SpacingX,
            result.SpacingY,
            result.LinkAxes);
        GridSpacingPresets.Add(item);
        SelectedGridSpacingPreset = item;
        NotifyListCommandStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditGridSpacingPreset))]
    private async Task EditGridSpacingPresetAsync()
    {
        var current = SelectedGridSpacingPreset;
        if (current is null)
            return;

        var result = await _dialogService.ShowGridSpacingPresetDialogAsync(
            new GridSpacingPresetDialogRequest(
                true,
                current.Name,
                current.SpacingX,
                current.SpacingY,
                current.LinkAxes,
                GetUsedNames(current)));
        if (result is null)
            return;

        var replacement = new GridSpacingPresetItemViewModel(
            current.Id,
            result.Name,
            result.SpacingX,
            result.SpacingY,
            result.LinkAxes);
        var index = GridSpacingPresets.IndexOf(current);
        GridSpacingPresets[index] = replacement;
        if (ReferenceEquals(SelectedMajorGridPreset, current))
            SelectedMajorGridPreset = replacement;
        if (ReferenceEquals(SelectedMinorGridPreset, current))
            SelectedMinorGridPreset = replacement;
        SelectedGridSpacingPreset = replacement;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGridSpacingPreset))]
    private void DeleteGridSpacingPreset()
    {
        var current = SelectedGridSpacingPreset;
        if (current is null || GridSpacingPresets.Count <= 2)
            return;

        var index = GridSpacingPresets.IndexOf(current);
        GridSpacingPresets.RemoveAt(index);
        if (ReferenceEquals(SelectedMajorGridPreset, current))
            SelectedMajorGridPreset = GridSpacingPresets.FirstOrDefault();
        if (ReferenceEquals(SelectedMinorGridPreset, current))
            SelectedMinorGridPreset = GridSpacingPresets.LastOrDefault();
        SelectedGridSpacingPreset = GridSpacingPresets[Math.Min(index, GridSpacingPresets.Count - 1)];
        NotifyListCommandStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveGridSpacingPresetUp))]
    private void MoveGridSpacingPresetUp()
    {
        var item = SelectedGridSpacingPreset!;
        var index = GridSpacingPresets.IndexOf(item);
        GridSpacingPresets.Move(index, index - 1);
        NotifyListCommandStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveGridSpacingPresetDown))]
    private void MoveGridSpacingPresetDown()
    {
        var item = SelectedGridSpacingPreset!;
        var index = GridSpacingPresets.IndexOf(item);
        GridSpacingPresets.Move(index, index + 1);
        NotifyListCommandStateChanged();
    }

    partial void OnSelectedGridSpacingPresetChanged(GridSpacingPresetItemViewModel? value)
    {
        NotifyListCommandStateChanged();
    }

    private bool CanEditGridSpacingPreset() => SelectedGridSpacingPreset is not null;
    private bool CanDeleteGridSpacingPreset() => SelectedGridSpacingPreset is not null && GridSpacingPresets.Count > 2;
    private bool CanMoveGridSpacingPresetUp() =>
        SelectedGridSpacingPreset is not null && GridSpacingPresets.IndexOf(SelectedGridSpacingPreset) > 0;
    private bool CanMoveGridSpacingPresetDown() =>
        SelectedGridSpacingPreset is not null &&
        GridSpacingPresets.IndexOf(SelectedGridSpacingPreset) is var index &&
        index >= 0 && index < GridSpacingPresets.Count - 1;

    private void Load(CadGridSettings settings)
    {
        GridType = (ViewModelCadGridType)settings.Type;
        GridSpacingPresets.Clear();
        foreach (var preset in settings.SpacingPresets)
            GridSpacingPresets.Add(GridSpacingPresetItemViewModel.From(preset));

        var major = FindById(settings.MajorSpacingPresetId)
                    ?? FindBySpacing(settings.SpacingX, settings.SpacingY)
                    ?? AddLocalPreset(settings.SpacingX, settings.SpacingY);
        var minor = FindById(settings.MinorSpacingPresetId)
                    ?? FindBySpacing(settings.GetMinorSpacingX(), settings.GetMinorSpacingY())
                    ?? AddLocalPreset(settings.GetMinorSpacingX(), settings.GetMinorSpacingY());
        SelectedMajorGridPreset = major;
        SelectedMinorGridPreset = minor;
        SelectedGridSpacingPreset = major;
        GridMinimumScreenSpacing = settings.MinimumScreenSpacing;
        GridMinorLineColor = settings.MinorLineColor;
        GridMajorLineColor = settings.MajorLineColor;
        GridMinorLineWidth = settings.MinorLineWidth;
        GridMajorLineWidth = settings.MajorLineWidth;
        SnapMarkerType = (ViewModelCadSnapMarkerType)settings.SnapMarkerType;
        SnapMarkerColor = settings.SnapMarkerColor;
        SnapMarkerLength = settings.SnapMarkerLength;
        SnapMarkerStrokeWidth = settings.SnapMarkerStrokeWidth;
        NotifyListCommandStateChanged();
    }

    private GridSpacingPresetItemViewModel AddLocalPreset(double spacingX, double spacingY)
    {
        var item = new GridSpacingPresetItemViewModel(
            Guid.NewGuid(),
            string.Empty,
            spacingX,
            spacingY,
            NearlyEqual(spacingX, spacingY));
        GridSpacingPresets.Add(item);
        return item;
    }

    private GridSpacingPresetItemViewModel? FindById(Guid? id) =>
        id is null ? null : GridSpacingPresets.FirstOrDefault(item => item.Id == id.Value);

    private GridSpacingPresetItemViewModel? FindBySpacing(double spacingX, double spacingY) =>
        GridSpacingPresets.FirstOrDefault(item =>
            NearlyEqual(item.SpacingX, spacingX) && NearlyEqual(item.SpacingY, spacingY));

    private IReadOnlyList<string> GetUsedNames(GridSpacingPresetItemViewModel? except = null) =>
        GridSpacingPresets
            .Where(item => !ReferenceEquals(item, except) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name)
            .ToArray();

    private void NotifyListCommandStateChanged()
    {
        EditGridSpacingPresetCommand.NotifyCanExecuteChanged();
        DeleteGridSpacingPresetCommand.NotifyCanExecuteChanged();
        MoveGridSpacingPresetUpCommand.NotifyCanExecuteChanged();
        MoveGridSpacingPresetDownCommand.NotifyCanExecuteChanged();
    }

    private static bool TryResolveSubdivision(double major, double minor, out int subdivision)
    {
        subdivision = 0;
        if (!IsValidRatio(major, minor))
            return false;
        subdivision = (int)Math.Round(major / minor, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool IsValidRatio(double major, double minor)
    {
        if (!IsSpacingValid(major) || !IsSpacingValid(minor))
            return false;
        var ratio = major / minor;
        return ratio >= CadGridSettings.MinimumSubdivision &&
               ratio <= CadGridSettings.MaximumSubdivision &&
               NearlyEqual(ratio, Math.Round(ratio));
    }

    private static bool IsSpacingValid(double value) =>
        value >= CadGridSettings.MinimumSpacingMillimeters &&
        value <= CadGridSettings.MaximumSpacingMillimeters &&
        double.IsFinite(value);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;
}

public sealed record GridSpacingPresetItemViewModel(
    Guid Id,
    string Name,
    double SpacingX,
    double SpacingY,
    bool LinkAxes,
    bool OpensGridSettings = false)
{
    public string DisplayName
    {
        get
        {
            if (OpensGridSettings)
                return Strings.EditGridSpacingPreset;

            var spacing = NearlyEqual(SpacingX, SpacingY)
                ? $"{SpacingX:0.###} mm"
                : $"{SpacingX:0.###} x {SpacingY:0.###} mm";
            return string.IsNullOrWhiteSpace(Name) ? spacing : $"{Name}: {spacing}";
        }
    }

    public CadGridSpacingPreset ToModel() => new(Id, Name, SpacingX, SpacingY, LinkAxes);

    public static GridSpacingPresetItemViewModel CreateGridSettingsAction() =>
        new(Guid.Empty, string.Empty, 0, 0, true, true);

    public static GridSpacingPresetItemViewModel From(CadGridSpacingPreset preset) =>
        new(preset.Id, preset.Name, preset.SpacingX, preset.SpacingY, preset.LinkAxes);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;
}
