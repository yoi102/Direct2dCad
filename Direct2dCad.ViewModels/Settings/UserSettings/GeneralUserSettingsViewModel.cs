using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public sealed record UserCultureOption(int Lcid, string Name);

public partial class GeneralUserSettingsViewModel : UserSettingsSectionViewModel
{
    public GeneralUserSettingsViewModel(CadGeneralUserSettings settings)
        : base(Localized("General"))
    {
        CultureOptions =
        [
            new UserCultureOption(1033, Strings.English),
            new UserCultureOption(1041, Strings.Japanese),
            new UserCultureOption(2052, Strings.Chinese)
        ];
        Load(settings);
    }

    private void Load(CadGeneralUserSettings settings)
    {
        IsDarkTheme = settings.IsDarkTheme;
        IsAutoRecoveryEnabled = settings.IsAutoRecoveryEnabled;
        HistoryBudgetMegabytes = settings.HistoryBudgetMegabytes;
        PrimaryColor = settings.PrimaryColor;
        SecondaryColor = settings.SecondaryColor;
        SelectedCulture = CultureOptions.FirstOrDefault(x => x.Lcid == settings.CultureLcid) ?? CultureOptions[0];
    }

    public IReadOnlyList<UserCultureOption> CultureOptions { get; }

    [ObservableProperty] public partial bool IsDarkTheme { get; set; }
    [ObservableProperty] public partial bool IsAutoRecoveryEnabled { get; set; }
    [ObservableProperty] public partial int HistoryBudgetMegabytes {get;set;}

    [ObservableProperty] public partial CadColor PrimaryColor { get; set; }

    [ObservableProperty] public partial CadColor SecondaryColor { get; set; }

    [ObservableProperty] public partial UserCultureOption? SelectedCulture { get; set; }

    internal override bool TryApplyTo(CadUserSettings settings)
    {
        if (SelectedCulture is null)
            return false;

        settings.General.IsDarkTheme = IsDarkTheme;
        settings.General.IsAutoRecoveryEnabled = IsAutoRecoveryEnabled;
        if(HistoryBudgetMegabytes is <16 or >4096)return false;
        settings.General.HistoryBudgetMegabytes=HistoryBudgetMegabytes;
        settings.General.CultureLcid = SelectedCulture.Lcid;
        settings.General.PrimaryColor = PrimaryColor;
        settings.General.SecondaryColor = SecondaryColor;
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadGeneralUserSettings());
    }
}
