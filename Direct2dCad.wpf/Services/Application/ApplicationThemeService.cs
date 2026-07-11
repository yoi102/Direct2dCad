using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MaterialDesignThemes.Wpf;
using MessagePipe;
using MediaColor = System.Windows.Media.Color;

namespace Direct2dCad.wpf.Services.Application;

internal sealed class ApplicationThemeService : IApplicationThemeService
{
    private readonly PaletteHelper _paletteHelper = new();
    private readonly IPublisher<ThemeChangedEvent> _publisher;

    public ApplicationThemeService(IPublisher<ThemeChangedEvent> publisher)
    {
        _publisher = publisher;
    }

    public bool IsDarkTheme => CurrentTheme.GetBaseTheme() == BaseTheme.Dark;

    public CadColor PrimaryColor => ToCadColor(CurrentTheme.PrimaryMid.Color);

    public CadColor SecondaryColor => ToCadColor(CurrentTheme.SecondaryMid.Color);

    private Theme CurrentTheme => _paletteHelper.GetTheme();

    public void ApplyThemeLightDark(bool isDarkTheme)
    {
        var theme = CurrentTheme;
        SetBaseTheme(theme, isDarkTheme);
        Commit(theme, isDarkTheme);
    }

    public void ApplyThemeColors(CadColor primaryColor, CadColor secondaryColor)
    {
        var theme = CurrentTheme;
        theme.SetPrimaryColor(ToMediaColor(primaryColor));
        theme.SetSecondaryColor(ToMediaColor(secondaryColor));
        Commit(theme, theme.GetBaseTheme() == BaseTheme.Dark);
    }

    public void ApplyTheme(bool isDarkTheme, CadColor primaryColor, CadColor secondaryColor)
    {
        var theme = CurrentTheme;
        SetBaseTheme(theme, isDarkTheme);
        theme.SetPrimaryColor(ToMediaColor(primaryColor));
        theme.SetSecondaryColor(ToMediaColor(secondaryColor));
        Commit(theme, isDarkTheme);
    }

    public void ToggleThemeLightDark()
    {
        ApplyThemeLightDark(!IsDarkTheme);
    }

    private void Commit(Theme theme, bool isDarkTheme)
    {
        _paletteHelper.SetTheme(theme);
        _publisher.Publish(new ThemeChangedEvent(isDarkTheme));
    }

    private static void SetBaseTheme(Theme theme, bool isDarkTheme)
    {
        if (isDarkTheme)
            theme.SetDarkTheme();
        else
            theme.SetLightTheme();
    }

    private static MediaColor ToMediaColor(CadColor color) =>
        MediaColor.FromArgb(color.A, color.R, color.G, color.B);

    private static CadColor ToCadColor(MediaColor color) =>
        CadColor.FromArgb(color.A, color.R, color.G, color.B);
}
