using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentOriginSettingsViewModel : DocumentSettingsSectionViewModel
{
    private readonly CadUnit _unit;

    public DocumentOriginSettingsViewModel(CadOriginSettings settings, CadUnit unit = CadUnit.Millimeter)
        : base(Strings.Origin)
    {
        _unit = unit;
        Load(settings);
    }

    private void Load(CadOriginSettings settings)
    {
        OriginDisplayType = (ViewModelCadOriginDisplayType)settings.DisplayType;
        OriginMarkerType = (ViewModelCadOriginMarkerType)settings.MarkerType;
        OriginLinePattern = (ViewModelCadOriginLinePattern)settings.LinePattern;
        OriginColor = settings.Color;
        OriginX = CadUnitConversion.FromMillimeters(settings.Position.X, _unit);
        OriginY = CadUnitConversion.FromMillimeters(settings.Position.Y, _unit);
        OriginSize = settings.Size;
        OriginStrokeWidth = settings.StrokeWidth;
    }

    [ObservableProperty] public partial ViewModelCadOriginDisplayType OriginDisplayType { get; set; }
    [ObservableProperty] public partial ViewModelCadOriginMarkerType OriginMarkerType { get; set; }
    [ObservableProperty] public partial ViewModelCadOriginLinePattern OriginLinePattern { get; set; }
    [ObservableProperty] public partial CadColor OriginColor { get; set; }
    [ObservableProperty] public partial double OriginX { get; set; }
    [ObservableProperty] public partial double OriginY { get; set; }
    [ObservableProperty] public partial double OriginSize { get; set; }
    [ObservableProperty] public partial double OriginStrokeWidth { get; set; }

    internal override bool TryApplyTo(CadViewSettings settings)
    {
        var x = CadUnitConversion.ToMillimeters(OriginX, _unit);
        var y = CadUnitConversion.ToMillimeters(OriginY, _unit);
        if (!IsFinite(x) || !IsFinite(y) ||
            !IsPositiveFinite(OriginSize) || !IsPositiveFinite(OriginStrokeWidth))
        {
            return false;
        }

        var origin = settings.Origin;
        origin.DisplayType = (CadOriginDisplayType)OriginDisplayType;
        origin.MarkerType = (CadOriginMarkerType)OriginMarkerType;
        origin.LinePattern = (CadOriginLinePattern)OriginLinePattern;
        origin.Color = OriginColor;
        origin.Position = new CadPointD(x, y);
        origin.Size = OriginSize;
        origin.StrokeWidth = OriginStrokeWidth;
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadOriginSettings());
    }
}
