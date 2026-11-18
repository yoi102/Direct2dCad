using System.IO;
using System.Windows;
using System.Net.Http;
using Antelcat.I18N.WPF;
using AvalonDock;
using AvalonDock.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Agent.Codex;
using Direct2dCad.CommandLine;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Services.Application;
using Direct2dCad.wpf.Services.Dialogs;
using Direct2dCad.wpf.Services.Importing;
using Direct2dCad.wpf.Services.Notifications;
using Direct2dCad.wpf.Services.Ole;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.wpf.Services.Toolboxes;
using Microsoft.Extensions.DependencyInjection;
using Direct2dCad.Mcp;
using Direct2dCad.Application.Tools;
using System.Windows.Threading;
using Direct2dCad.Application.Platform;

namespace Direct2dCad;

public partial class App : System.Windows.Application
{
    private IServiceProvider _serviceProvider;
    private readonly WpfBindingDiagnostics? _bindingDiagnostics;

    public App()
    {
        WpfCrashLog.Install(Dispatcher);
        _bindingDiagnostics = WpfBindingDiagnostics.StartFromEnvironment();
        string lang = System.Globalization.CultureInfo.CurrentCulture.Name;
        var culture = new System.Globalization.CultureInfo(lang);
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
        this.MainWindow = mainWindow;

        mainWindow.Show();
        _ = StartMcpAsync(e.Args);
    }

    private async Task StartMcpAsync(string[] arguments)
    {
        try
        {
            await _serviceProvider.GetRequiredService<MainViewModel>()
                .InitializeMcpAsync(
                    CadMcpServer.GetRequestedPort(arguments),
                    CadMcpServer.GetRequestedAccessToken(arguments));
        }
        catch (Exception exception)
        {
            _serviceProvider.GetRequiredService<ICadMessageLog>().Add(exception.Message, CadMessageLevel.Error, "MCP");
        }
    }
    private void ConfigureServices(IServiceCollection services)
    {
        services.AddViewModels();
        services.AddSingleton(CadRenderResourceBudget.Shared);
        services.AddSingleton<ICadRenderSessionFactory, Direct2DRenderSessionFactory>();
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
            dock.AddToolbox<DrawingRecoveryToolboxViewModel>();
            dock.AddToolbox<EntitySearchToolboxViewModel>();
            dock.AddToolbox<SelectionFilterToolboxViewModel>();
            dock.AddToolbox<CommandLineToolboxViewModel>();
            dock.AddToolbox<MessageToolboxViewModel>();
            dock.AddToolbox<AiAssistantToolboxViewModel>();
        });

        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddSingleton<IFileLocationService, FileLocationService>();
        services.AddSingleton<IImageImportService, ImageImportService>();
        services.AddSingleton<ICadViewCaptureService, CadViewCaptureService>();
        services.AddSingleton<IAiFileImportService, AiFileImportService>();
        services.AddSingleton<IClipboardTextService, ClipboardTextService>();
        services.AddSingleton<ICadMcpService>(sp =>
        {
            var service = new CadMcpService(new DispatcherSynchronizationContext(Dispatcher),
                sp.GetRequiredService<ICadToolWorkspace>(), sp.GetService<IImageImportService>(),
                sp.GetService<MessagePipe.IAsyncPublisher<CadToolActivityMessage>>());
            var log = sp.GetRequiredService<ICadMessageLog>();
            service.StatusChanged += (_, _) =>
            {
                if (service.Status is { State: CadMcpState.Running, Endpoint: { } endpoint })
                    log.Add($"MCP listening at {endpoint}", source: "MCP");
                else if (service.Status is { State: CadMcpState.Failed, Error: { } error })
                    log.Add(error, CadMessageLevel.Error, "MCP");
            };
            return service;
        });
        services.AddSingleton<IOleHostService, OleHostService>();
        services.AddSingleton<ICadPrintService, CadPrintService>();
        services.AddSingleton<IDialogService, DialogService>();
        var settings = ApplicationSettingsPathResolver.Location;
        // Launch-only MCP values (--mcp-port, --mcp-token, the stdio bridge) must not reach the saved file.
        services.AddSingleton<IUserSettingsStore>(_ =>
            new SessionMcpUserSettingsStore(new JsonUserSettingsStore(settings.GetPath("user-settings.json"))));
        services.AddSingleton<IWorkspaceSettingsStore>(_ => new JsonWorkspaceSettingsStore(settings.GetPath("workspace-settings.json")));
        services.AddSingleton<IToolboxLayoutSettingsStore>(_ => new JsonToolboxLayoutSettingsStore(settings.GetPath("toolbox-settings.json")));
        // The WPF host has always kept AI settings under LocalApplicationData.
        services.AddSingleton<IAiAssistantSettingsStore>(_ => new JsonAiAssistantSettingsStore(settings.IsOverridden
            ? settings.GetPath("ai-assistant-settings.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Direct2dCad", "ai-assistant-settings.json")));
        services.AddSingleton<ToolboxLayoutPersistenceService>();
        services.AddSingleton<ISystemFontCatalog, WpfSystemFontCatalog>();
        services.AddSingleton<ICadMessageLog, CadMessageLog>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IToolboxIconProvider, ToolboxIconProvider>();
        services.AddSingleton<ICadCommandLineService, CadCommandLineService>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IAiChatClient, LmStudioChatClient>();
        services.AddSingleton<ICodexAgentClient, CodexAppServerClient>();

        services.AddTransient<MainWindow>();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        (_serviceProvider.GetService<ICadMcpService>() as IDisposable)?.Dispose();
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
        _bindingDiagnostics?.Dispose();
    }
}
