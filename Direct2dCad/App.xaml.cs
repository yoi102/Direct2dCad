using Antelcat.I18N.WPF;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewServices.Abstractions;
using Direct2dCad.wpf.Services;
using Direct2dCad.wpf.ViewServices;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad;

public partial class App : System.Windows.Application
{
    public App()
    {
        string lang = System.Globalization.CultureInfo.CurrentCulture.Name;
        var culture = new System.Globalization.CultureInfo(lang);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        I18NExtension.Culture = culture;






        var services = new ServiceCollection();
        services.AddDirect2dCadEditor()
                .AddViewModels()
                .AddViewServices();

        services.AddTransient<ICultureSettingService, CultureSettingService>()
                .AddTransient<IThemeSettingService, ThemeSettingService>();
        services.AddMessagePipe();

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
    }
}
