using Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Application.Platform;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.Desktop;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Toolboxes;
using Microsoft.Extensions.DependencyInjection;

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
            var settings = SettingsPath.Location;
            services.AddDirect2dCadDesktop(new CadDesktopHostOptions
            {
                CreateUiSynchronizationContext = () => new global::Avalonia.Threading.AvaloniaSynchronizationContext(
                    global::Avalonia.Threading.Dispatcher.UIThread, global::Avalonia.Threading.DispatcherPriority.Normal),
                Settings = settings,
                AiSettingsPath = settings.GetPath("ai-assistant-settings.json"),
                // The first Avalonia run adopts the WPF host's preferences.
                UserSettingsFallbackPath = settings.IsOverridden ? null : SettingsPath.WpfUserSettingsPath,
            });
            services.AddSingleton<IApplicationCultureService, CultureService>();
            services.AddSingleton<IApplicationThemeService, ThemeService>();
            services.AddSingleton<IFileDialogService, FileDialogs>();
            services.AddSingleton<IFileLocationService, FileLocationService>();
            services.AddSingleton<IImageImportService, ImageImportService>();
            services.AddSingleton<ICadViewCaptureService, ViewCaptureService>();
            services.AddSingleton<IAiFileImportService, AiFileImportService>();
            services.AddSingleton<IClipboardTextService, ClipboardTextService>();
            services.AddSingleton<IOleHostService, OleHostService>();
            services.AddSingleton<ICadPrintService, WindowsPrintService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ISystemFontCatalog, FontCatalog>();
            services.AddSingleton<SnackbarService>();
            services.AddSingleton<ISnackbarService>(sp => sp.GetRequiredService<SnackbarService>());
            services.AddSingleton<IToolboxIconProvider, IconProvider>();
            var toolboxes = new ToolboxRegistrar(services);
            CadToolboxCatalog.Register(toolboxes);
            services.AddSingleton<IDockLayoutService>(sp => new DockLayoutService(toolboxes.Resolve(sp)));
            services.AddSingleton<SideToggleManager>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Ioc.Default.ConfigureServices(_services);
            Window = new MainWindow(_services.GetRequiredService<MainViewModel>(), _services.GetRequiredService<SnackbarService>(), _services.GetRequiredService<IToolboxLayoutSettingsStore>(), _services.GetRequiredService<IDialogService>());
            desktop.MainWindow = Window;
            Window.Opened += async (_, _) => await CadDesktopServices.StartMcpAsync(_services, desktop.Args ?? []);
            if (desktop.Args is { } args && Array.FindIndex(args, a => a is "--smoke-test" or "--smoke-test-headless") is var index && index >= 0)
            {
                var output = Path.GetFullPath(args.Length > index + 1 ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "native-smoke.json"));
                Window.Opened += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(async () => await NativeSmoke.RunAsync(Window, _services, output));
            }
            desktop.Exit += (_, _) => CadDesktopServices.DisposeServices(_services);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Registers each catalog toolbox as a singleton and hands them to the dock service in catalog order.</summary>
    private sealed class ToolboxRegistrar(IServiceCollection services) : ICadToolboxRegistrar
    {
        private readonly List<Func<IServiceProvider, IToolbox>> _resolvers = [];

        public void Add<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TToolbox>()
            where TToolbox : CadToolboxViewModelBase
        {
            services.AddSingleton<TToolbox>();
            _resolvers.Add(sp => sp.GetRequiredService<TToolbox>());
        }

        public IToolbox[] Resolve(IServiceProvider sp) => [.. _resolvers.Select(resolve => resolve(sp))];
    }
}
