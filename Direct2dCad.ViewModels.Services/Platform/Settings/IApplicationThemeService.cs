using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.Services.Platform;

public interface IApplicationThemeService
{
    bool IsDarkTheme { get; }
    CadColor PrimaryColor { get; }
    CadColor SecondaryColor { get; }

    void ToggleThemeLightDark();

    void ApplyThemeLightDark(bool isDarkTheme);

    void ApplyThemeColors(CadColor primaryColor, CadColor secondaryColor);

    void ApplyTheme(bool isDarkTheme, CadColor primaryColor, CadColor secondaryColor);
}
