using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public abstract class UserSettingsSectionViewModel : ObservableObject
{
    protected UserSettingsSectionViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    internal abstract bool TryApplyTo(CadUserSettings settings);

    protected static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    protected static string Localized(string key) =>
        Strings.ResourceManager.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key;
}
