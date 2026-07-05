using Antelcat.I18N.WPF;
using Direct2dCad.ViewModels.Services.ViewServices;

namespace Direct2dCad.wpf.Services;

internal class CultureSettingService : ICultureSettingService
{
    public void ChangeCulture(string language)
    {
        var culture = new System.Globalization.CultureInfo(language);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        I18NExtension.Culture = culture;
    }

    public void ChangeCulture(int lcid)
    {
        var culture = new System.Globalization.CultureInfo(lcid);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        I18NExtension.Culture = culture;
    }

    public int GetCurrentCultureLCID()
    {
        return Thread.CurrentThread.CurrentUICulture.LCID;
    }

}
