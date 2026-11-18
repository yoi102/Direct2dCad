using System.IO;
using System.Windows;
using System.Windows.Threading;
using Antelcat.I18N.WPF;
using AvalonDock;
using AvalonDock.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Application.Platform;
using Direct2dCad.Desktop;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Services.Application;
using Direct2dCad.wpf.Services.Dialogs;
using Direct2dCad.wpf.Services.Importing;
using Direct2dCad.wpf.Services.Notifications;
using Direct2dCad.wpf.Services.Ole;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.wpf.Services.Toolboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad;

public partial class App : System.Windows.Application
{
    private readonly ServiceProvider _serviceProvider;
    private readonly WpfBindingDiagnostics? _bindingDiagnostics;

    public App()
    {
        WpfCrashLog.Install(Dispatcher);
        _bindingDiagnostics = WpfBindingDiagnostics.StartFromEnvironment();
        var culture = new System.Globalization.CultureInfo(System.Globalization.CultureInfo.CurrentCulture.Name);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        I18NExtension.Culture = culture;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Ioc.Default.ConfigureServices(_serviceProvider);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = CadDesktopServices.StartMcpAsync(_serviceProvider, e.Args);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var settings = ApplicationSettingsPathResolver.Location;
        services.AddDirect2dCadDesktop(new CadDesktopHostOptions
        {
            CreateUiSynchronizationContext = () => new DispatcherSynchronizationContext(Dispatcher),
            Settings = settings,
            // The WPF host has always kept AI settings under LocalApplicationData.
            AiSettingsPath = settings.IsOverridden
                ? settings.GetPath("ai-assistant-settings.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Direct2dCad", "ai-assistant-settings.json"),
        });

        services.AddSingleton<IApplicationCultureService, ApplicationCultureService>()
                .AddSingleton<IApplicationThemeService, ApplicationThemeService>();

        services.AddDockLayoutService(configure: dock =>
        {
            dock.ConfigureToggleDock(opts =>
            {
                opts.ButtonSize = 28;
                opts.DefaultDockWidth = 280;
                opts.DefaultDockHeight = 220;
                opts.LayoutPriority = nameof(DockLayoutPriority.BottomFullWidth);
            });
            CadToolboxCatalog.Register(new DockToolboxRegistrar(dock));
        });

        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddSingleton<IFileLocationService, FileLocationService>();
        services.AddSingleton<IImageImportService, ImageImportService>();
        services.AddSingleton<ICadViewCaptureService, CadViewCaptureService>();
        services.AddSingleton<IAiFileImportService, AiFileImportService>();
        services.AddSingleton<IClipboardTextService, ClipboardTextService>();
        services.AddSingleton<IOleHostService, OleHostService>();
        services.AddSingleton<ICadPrintService, CadPrintService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ToolboxLayoutPersistenceService>();
        services.AddSingleton<ISystemFontCatalog, WpfSystemFontCatalog>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IToolboxIconProvider, ToolboxIconProvider>();

        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CadDesktopServices.DisposeServices(_serviceProvider);
        base.OnExit(e);
        _bindingDiagnostics?.Dispose();
    }

    private sealed class DockToolboxRegistrar(DockLayoutBuilder dock) : ICadToolboxRegistrar
    {
        public void Add<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TToolbox>()
            where TToolbox : CadToolboxViewModelBase => dock.AddToolbox<TToolbox>();
    }
}
