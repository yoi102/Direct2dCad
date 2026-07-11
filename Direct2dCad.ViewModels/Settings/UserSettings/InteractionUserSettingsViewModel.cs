using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public partial class InteractionUserSettingsViewModel : UserSettingsSectionViewModel
{
    public InteractionUserSettingsViewModel(CadInteractionUserSettings settings)
        : base(Localized("Interaction"))
    {
        SelectedEntityStrokeColor = settings.SelectedEntityStrokeColor;
        SelectedEntityStrokeWidth = settings.SelectedEntityStrokeWidth;
        GripStrokeColor = settings.GripStrokeColor;
        GripFillColor = settings.GripFillColor;
        GripSize = settings.GripSize;
        GripStrokeWidth = settings.GripStrokeWidth;
        GripPreviewStrokeColor = settings.GripPreviewStrokeColor;
        GripPreviewFillColor = settings.GripPreviewFillColor;
        GripPreviewStrokeWidth = settings.GripPreviewStrokeWidth;
        SelectionWindowStrokeColor = settings.SelectionWindowStrokeColor;
        SelectionWindowFillColor = settings.SelectionWindowFillColor;
        SelectionWindowStrokeWidth = settings.SelectionWindowStrokeWidth;
        SelectionCrossingStrokeColor = settings.SelectionCrossingStrokeColor;
        SelectionCrossingFillColor = settings.SelectionCrossingFillColor;
        SelectionCrossingStrokeWidth = settings.SelectionCrossingStrokeWidth;
    }

    [ObservableProperty] public partial CadColor SelectedEntityStrokeColor { get; set; }
    [ObservableProperty] public partial double SelectedEntityStrokeWidth { get; set; }
    [ObservableProperty] public partial CadColor GripStrokeColor { get; set; }
    [ObservableProperty] public partial CadColor GripFillColor { get; set; }
    [ObservableProperty] public partial double GripSize { get; set; }
    [ObservableProperty] public partial double GripStrokeWidth { get; set; }
    [ObservableProperty] public partial CadColor GripPreviewStrokeColor { get; set; }
    [ObservableProperty] public partial CadColor GripPreviewFillColor { get; set; }
    [ObservableProperty] public partial double GripPreviewStrokeWidth { get; set; }
    [ObservableProperty] public partial CadColor SelectionWindowStrokeColor { get; set; }
    [ObservableProperty] public partial CadColor SelectionWindowFillColor { get; set; }
    [ObservableProperty] public partial double SelectionWindowStrokeWidth { get; set; }
    [ObservableProperty] public partial CadColor SelectionCrossingStrokeColor { get; set; }
    [ObservableProperty] public partial CadColor SelectionCrossingFillColor { get; set; }
    [ObservableProperty] public partial double SelectionCrossingStrokeWidth { get; set; }

    internal override bool TryApplyTo(CadUserSettings settings)
    {
        if (!IsPositiveFinite(SelectedEntityStrokeWidth) ||
            !IsPositiveFinite(GripSize) ||
            !IsPositiveFinite(GripStrokeWidth) ||
            !IsPositiveFinite(GripPreviewStrokeWidth) ||
            !IsPositiveFinite(SelectionWindowStrokeWidth) ||
            !IsPositiveFinite(SelectionCrossingStrokeWidth))
        {
            return false;
        }

        var interaction = settings.Interaction;
        interaction.SelectedEntityStrokeColor = SelectedEntityStrokeColor;
        interaction.SelectedEntityStrokeWidth = SelectedEntityStrokeWidth;
        interaction.GripStrokeColor = GripStrokeColor;
        interaction.GripFillColor = GripFillColor;
        interaction.GripSize = GripSize;
        interaction.GripStrokeWidth = GripStrokeWidth;
        interaction.GripPreviewStrokeColor = GripPreviewStrokeColor;
        interaction.GripPreviewFillColor = GripPreviewFillColor;
        interaction.GripPreviewStrokeWidth = GripPreviewStrokeWidth;
        interaction.SelectionWindowStrokeColor = SelectionWindowStrokeColor;
        interaction.SelectionWindowFillColor = SelectionWindowFillColor;
        interaction.SelectionWindowStrokeWidth = SelectionWindowStrokeWidth;
        interaction.SelectionCrossingStrokeColor = SelectionCrossingStrokeColor;
        interaction.SelectionCrossingFillColor = SelectionCrossingFillColor;
        interaction.SelectionCrossingStrokeWidth = SelectionCrossingStrokeWidth;
        return true;
    }
}
