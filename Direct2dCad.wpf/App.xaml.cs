using System.Windows;
using Antelcat.I18N.WPF;
using AvalonDock;
using AvalonDock.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.ViewServices;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad;

public partial class App : System.Windows.Application
{
    private IServiceProvider _serviceProvider;

    public App()
    {
        string lang = System.Globalization.CultureInfo.CurrentCulture.Name;
        var culture = new System.Globalization.CultureInfo(lang);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        I18NExtension.Culture = culture;


        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(_serviceProvider);


      
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        this.MainWindow = mainWindow;

        mainWindow.Show();
    }
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDirect2dCadEditor()
                .AddViewModels();
        services.AddSingleton<ICultureSettingService, CultureSettingService>()
                .AddSingleton<IThemeSettingService, ThemeSettingService>();
        services.AddMessagePipe();


        services.AddDockLayoutService(configure: dock =>
        {
            dock.ConfigureToggleDock(opts =>
            {
                opts.ButtonSize = 28;
                opts.DefaultDockWidth = 280;
                opts.DefaultDockHeight = 220;
                opts.LayoutPriority = nameof(DockLayoutPriority.BottomFullWidth);
            });

            // Register toolboxes — order determines sidebar button order
            dock.AddToolbox<FolderExplorerToolboxViewModel>();
            dock.AddToolbox<LayerToolboxViewModel>();
            dock.AddToolbox<EntityPropertiesToolboxViewModel>();
            dock.AddToolbox<SearchViewModel>();
        });

        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddSingleton<IImageImportService, ImageImportService>();
        services.AddSingleton<IOleImportService, OleImportService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IToolboxIconsService, ToolboxIconsService>();

        services.AddTransient<MainWindow>();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
