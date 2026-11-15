using Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Agent.Codex;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;
using Direct2dCad.CommandLine;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Direct2dCad.Application.Platform;
using Direct2dCad.Application.Tools;
using Direct2dCad.Mcp;

namespace Direct2dCad.Avalonia;

public partial class App : global::Avalonia.Application
{
    private ServiceProvider? _services;
    public static MainWindow? Window { get; private set; }
    public static nint OwnerHandle => Window?.TryGetPlatformHandle()?.Handle ?? 0;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddViewModels();
            NativeMessagePipe.Register(services);
            services.AddSingleton(CadRenderResourceBudget.Shared);
            services.AddSingleton<ICadRenderSessionFactory, Direct2DRenderSessionFactory>();
            services.AddSingleton<IApplicationCultureService, CultureService>();
            services.AddSingleton<IApplicationThemeService, ThemeService>();
            services.AddSingleton<IUserSettingsStore, UserSettingsStore>();
            services.AddSingleton<IWorkspaceSettingsStore, WorkspaceSettingsStore>();
            services.AddSingleton<IToolboxLayoutSettingsStore, ToolboxSettingsStore>();
            services.AddSingleton<IFileDialogService, FileDialogs>();
            services.AddSingleton<IFileLocationService, FileLocationService>();
            services.AddSingleton<IImageImportService, ImageImportService>();
            services.AddSingleton<ICadViewCaptureService, ViewCaptureService>();
            services.AddSingleton<IAiFileImportService, AiFileImportService>();
            services.AddSingleton<IClipboardTextService, ClipboardTextService>();
            services.AddSingleton<ICadMcpService>(sp =>
            {
                var service = new CadMcpService(new global::Avalonia.Threading.AvaloniaSynchronizationContext(
                    global::Avalonia.Threading.Dispatcher.UIThread, global::Avalonia.Threading.DispatcherPriority.Normal),
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
            services.AddSingleton<ICadPrintService, WindowsPrintService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ISystemFontCatalog, FontCatalog>();
            services.AddSingleton<ICadMessageLog, CadMessageLog>();
            services.AddSingleton<SnackbarService>();
            services.AddSingleton<ISnackbarService>(sp => sp.GetRequiredService<SnackbarService>());
            services.AddSingleton<IToolboxIconProvider, IconProvider>();
            services.AddSingleton<ICadCommandLineService, CadCommandLineService>();
            services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
            services.AddSingleton<IAiChatClient, LmStudioChatClient>();
            services.AddSingleton<ICodexAgentClient, CodexAppServerClient>();
            services.AddSingleton<IAiAssistantSettingsStore, AiSettingsStore>();
            services.AddSingleton<DocumentExplorerToolboxViewModel>(); services.AddSingleton<LayersToolboxViewModel>();
            services.AddSingleton<BlocksToolboxViewModel>(); services.AddSingleton<EntityPropertiesToolboxViewModel>();
            services.AddSingleton<DrawingRecoveryToolboxViewModel>(); services.AddSingleton<EntitySearchToolboxViewModel>();
            services.AddSingleton<SelectionFilterToolboxViewModel>(); services.AddSingleton<CommandLineToolboxViewModel>();
            services.AddSingleton<MessageToolboxViewModel>(); services.AddSingleton<AiAssistantToolboxViewModel>();
            services.AddSingleton<IDockLayoutService>(sp => new DockLayoutService(new IToolbox[] {
                sp.GetRequiredService<DocumentExplorerToolboxViewModel>(), sp.GetRequiredService<LayersToolboxViewModel>(),
                sp.GetRequiredService<BlocksToolboxViewModel>(), sp.GetRequiredService<EntityPropertiesToolboxViewModel>(),
                sp.GetRequiredService<DrawingRecoveryToolboxViewModel>(), sp.GetRequiredService<EntitySearchToolboxViewModel>(),
                sp.GetRequiredService<SelectionFilterToolboxViewModel>(), sp.GetRequiredService<CommandLineToolboxViewModel>(),
                sp.GetRequiredService<MessageToolboxViewModel>(), sp.GetRequiredService<AiAssistantToolboxViewModel>() }));
            services.AddSingleton<SideToggleManager>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Ioc.Default.ConfigureServices(_services);
            Window = new MainWindow(_services.GetRequiredService<MainViewModel>(), _services.GetRequiredService<SnackbarService>(), _services.GetRequiredService<IToolboxLayoutSettingsStore>(), _services.GetRequiredService<IDialogService>());
            desktop.MainWindow = Window;
            Window.Opened += async (_, _) => await StartMcpAsync(desktop.Args ?? []);
            if (desktop.Args is { } args && Array.FindIndex(args, a => a is "--smoke-test" or "--smoke-test-headless") is var index && index >= 0)
            {
                var output = Path.GetFullPath(args.Length > index + 1 ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "native-smoke.json"));
                Window.Opened += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(async () => await NativeSmoke.RunAsync(Window, _services, output));
            }
            desktop.Exit += (_, _) =>
            {
                (_services.GetService<ICadMcpService>() as IDisposable)?.Dispose();
                _services.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartMcpAsync(string[] arguments)
    {
        try
        {
            await _services!.GetRequiredService<MainViewModel>()
                .InitializeMcpAsync(CadMcpServer.GetRequestedPort(arguments));
        }
        catch (Exception exception)
        {
            _services!.GetRequiredService<ICadMessageLog>().Add(exception.Message, CadMessageLevel.Error, "MCP");
        }
    }
}

