using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings;

public partial class DocumentDisplaySettingsViewModel : DocumentSettingsSectionViewModel
{
    public DocumentDisplaySettingsViewModel(CadViewSettings settings)
        : base(Strings.Display)
    {
        BackgroundColor = settings.BackgroundColor;
    }

    [ObservableProperty]
    public partial CadColor BackgroundColor { get; set; }

    internal override bool TryApplyTo(CadViewSettings settings)
    {
        settings.BackgroundColor = BackgroundColor;
        return true;
    }
}
