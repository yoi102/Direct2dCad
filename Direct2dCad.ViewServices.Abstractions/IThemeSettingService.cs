using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewServices.Abstractions;

public interface IThemeSettingService
{
    bool IsDarkTheme { get; }

    void ToggleThemeLightDark();

    void ApplyThemeLightDark(bool isDarkTheme);
}
