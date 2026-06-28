using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.ViewServices;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad;

public partial class App : System.Windows.Application
{
    public App()
    {
        var cultureName = System.Globalization.CultureInfo.CurrentCulture.Name;
        var culture = new System.Globalization.CultureInfo(cultureName);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        var services = new ServiceCollection()
            .AddDirect2dCadEditor()
            .AddViewModels()
            .AddViewServices();

        services.AddMessagePipe();

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
    }
}
