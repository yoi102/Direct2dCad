using System.Globalization;

namespace Direct2dCad.Lang;

public static class CadUiText
{
    public static string Get(string key) => Strings.Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
