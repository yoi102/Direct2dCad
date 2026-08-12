using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings;

public partial class GridSpacingPresetEditorViewModel : ObservableObject
{
    private readonly HashSet<string> _unavailableNames;
    private readonly CadUnit _unit;
    private bool _synchronizingAxes;

    public GridSpacingPresetEditorViewModel(GridSpacingPresetDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IsEditing = request.IsEditing;
        _unit = request.Unit;
        _unavailableNames = request.UnavailableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Name = request.Name;
        SpacingX = CadUnitConversion.FromMillimeters(request.SpacingX, _unit);
        SpacingY = CadUnitConversion.FromMillimeters(request.SpacingY, _unit);
        LinkAxes = request.LinkAxes;
        if (LinkAxes)
            SpacingY = SpacingX;
        NotifyValidationChanged();
    }

    public bool IsEditing { get; }

    public string Title => Localize(IsEditing ? "EditGridSpacingPreset" : "AddGridSpacingPreset");
    public string UnitSymbol => CadUnitConversion.GetSymbol(_unit);
    public double MinimumSpacing => CadUnitConversion.FromMillimeters(CadGridSettings.MinimumSpacingMillimeters, _unit);
    public double MaximumSpacing => CadUnitConversion.FromMillimeters(CadGridSettings.MaximumSpacingMillimeters, _unit);

    public bool IsValid => string.IsNullOrEmpty(ValidationError);

    public string? ValidationError
    {
        get
        {
            var trimmedName = Name?.Trim();
            if (!string.IsNullOrEmpty(trimmedName) && _unavailableNames.Contains(trimmedName))
                return Localize("GridSpacingPresetNameExists");
            if (!IsSpacingValid(CadUnitConversion.ToMillimeters(SpacingX, _unit)) ||
                !IsSpacingValid(CadUnitConversion.ToMillimeters(SpacingY, _unit)))
            {
                return string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Localize("GridSpacingPresetRangeError"),
                    MinimumSpacing,
                    MaximumSpacing,
                    UnitSymbol);
            }
            return null;
        }
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial double SpacingX { get; set; } = 1.0;
    [ObservableProperty] public partial double SpacingY { get; set; } = 1.0;
    [ObservableProperty] public partial bool LinkAxes { get; set; } = true;

    public GridSpacingPresetDialogResult CreateResult()
    {
        return new GridSpacingPresetDialogResult(
            Name?.Trim() ?? string.Empty,
            CadUnitConversion.ToMillimeters(SpacingX, _unit),
            CadUnitConversion.ToMillimeters(LinkAxes ? SpacingX : SpacingY, _unit),
            LinkAxes);
    }

    partial void OnNameChanged(string value) => NotifyValidationChanged();

    partial void OnSpacingXChanged(double value)
    {
        if (LinkAxes && !_synchronizingAxes)
        {
            _synchronizingAxes = true;
            SpacingY = value;
            _synchronizingAxes = false;
        }

        NotifyValidationChanged();
    }

    partial void OnSpacingYChanged(double value) => NotifyValidationChanged();

    partial void OnLinkAxesChanged(bool value)
    {
        if (value)
            SpacingY = SpacingX;
        NotifyValidationChanged();
    }

    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
    }

    private static bool IsSpacingValid(double value) =>
        value >= CadGridSettings.MinimumSpacingMillimeters &&
        value <= CadGridSettings.MaximumSpacingMillimeters &&
        double.IsFinite(value);

    private static string Localize(string key) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
}
