using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public sealed record UserCultureOption(int Lcid, string Name);

public partial class GeneralUserSettingsViewModel : UserSettingsSectionViewModel
{
    public GeneralUserSettingsViewModel(CadGeneralUserSettings settings)
        : base(Localized("General"))
    {
        IsDarkTheme = settings.IsDarkTheme;
        CultureOptions =
        [
            new UserCultureOption(1033, Strings.English),
            new UserCultureOption(1041, Strings.Japanese),
            new UserCultureOption(2052, Strings.Chinese)
        ];
        SelectedCulture = CultureOptions.FirstOrDefault(x => x.Lcid == settings.CultureLcid) ?? CultureOptions[0];
    }

    public IReadOnlyList<UserCultureOption> CultureOptions { get; }

    [ObservableProperty] public partial bool IsDarkTheme { get; set; }

    [ObservableProperty] public partial UserCultureOption SelectedCulture { get; set; }

    internal override bool TryApplyTo(CadUserSettings settings)
    {
        if (SelectedCulture is null)
            return false;

        settings.General.IsDarkTheme = IsDarkTheme;
        settings.General.CultureLcid = SelectedCulture.Lcid;
        return true;
    }
}
