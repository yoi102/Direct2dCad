using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentOriginSettingsViewModel : DocumentSettingsSectionViewModel
{
    public DocumentOriginSettingsViewModel(CadOriginSettings settings)
        : base(Strings.Origin)
    {
        OriginDisplayType = (ViewModelCadOriginDisplayType)settings.DisplayType;
        OriginMarkerType = (ViewModelCadOriginMarkerType)settings.MarkerType;
        OriginLinePattern = (ViewModelCadOriginLinePattern)settings.LinePattern;
        OriginColor = settings.Color;
        OriginX = settings.Position.X;
        OriginY = settings.Position.Y;
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
        if (!IsFinite(OriginX) || !IsFinite(OriginY) ||
            !IsPositiveFinite(OriginSize) || !IsPositiveFinite(OriginStrokeWidth))
        {
            return false;
        }

        var origin = settings.Origin;
        origin.DisplayType = (CadOriginDisplayType)OriginDisplayType;
        origin.MarkerType = (CadOriginMarkerType)OriginMarkerType;
        origin.LinePattern = (CadOriginLinePattern)OriginLinePattern;
        origin.Color = OriginColor;
        origin.Position = new CadPointD(OriginX, OriginY);
        origin.Size = OriginSize;
        origin.StrokeWidth = OriginStrokeWidth;
        return true;
    }
}
