namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IThemeSettingService
{
    bool IsDarkTheme { get; }

    void ToggleThemeLightDark();

    void ApplyThemeLightDark(bool isDarkTheme);
}
