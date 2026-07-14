using System.Windows;
using Antelcat.I18N.WPF;
using AvalonDock;
using AvalonDock.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.CommandLine;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Services.Application;
using Direct2dCad.wpf.Services.Dialogs;
using Direct2dCad.wpf.Services.Importing;
using Direct2dCad.wpf.Services.Notifications;
using Direct2dCad.wpf.Services.Ole;
using Direct2dCad.wpf.Services.Toolboxes;
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
        services.AddSingleton<IApplicationCultureService, ApplicationCultureService>()
                .AddSingleton<IApplicationThemeService, ApplicationThemeService>();
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
            dock.AddToolbox<DocumentExplorerToolboxViewModel>();
            dock.AddToolbox<LayersToolboxViewModel>();
            dock.AddToolbox<BlocksToolboxViewModel>();
            dock.AddToolbox<EntityPropertiesToolboxViewModel>();
            dock.AddToolbox<EntitySearchToolboxViewModel>();
            dock.AddToolbox<SelectionFilterToolboxViewModel>();
            dock.AddToolbox<CommandLineToolboxViewModel>();
        });

        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddSingleton<IImageImportService, ImageImportService>();
        services.AddSingleton<IClipboardTextService, ClipboardTextService>();
        services.AddSingleton<IOleHostService, OleHostService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<IWorkspaceSettingsStore, JsonWorkspaceSettingsStore>();
        services.AddSingleton<IToolboxLayoutSettingsStore, JsonToolboxLayoutSettingsStore>();
        services.AddSingleton<ToolboxLayoutPersistenceService>();
        services.AddSingleton<ISystemFontCatalog, WpfSystemFontCatalog>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IToolboxIconProvider, ToolboxIconProvider>();
        services.AddSingleton<ICadCommandLineService, CadCommandLineService>();

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
