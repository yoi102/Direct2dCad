using Direct2dCad.Agent.Codex;
using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;
using Direct2dCad.Application.Platform;
using Direct2dCad.Application.Tools;
using Direct2dCad.CommandLine;
using Direct2dCad.Mcp;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Desktop;

/// <summary>What differs between the desktop hosts in the services they otherwise share.</summary>
public sealed class CadDesktopHostOptions
{
    /// <summary>Creates the UI-thread context MCP tool calls are dispatched to.</summary>
    public required Func<SynchronizationContext> CreateUiSynchronizationContext { get; init; }

    /// <summary>The host's settings directory (each host keeps its existing one).</summary>
    public required CadSettingsLocation Settings { get; init; }

    /// <summary>Full path of the AI assistant settings file.</summary>
    public required string AiSettingsPath { get; init; }

    /// <summary>Read when the host has no user-settings.json yet; see <see cref="JsonUserSettingsStore"/>.</summary>
    public string? UserSettingsFallbackPath { get; init; }
}

public static class CadDesktopServices
{
    /// <summary>
    /// Registers everything the WPF and Avalonia hosts compose identically: view models, MessagePipe, the Direct2D
    /// renderer, settings stores, MCP, the command line, the message log and the AI back ends. The host adds its
    /// platform services (dialogs, clipboard, OLE, printing, theming, culture, fonts, icons, docking) itself.
    /// </summary>
    public static IServiceCollection AddDirect2dCadDesktop(this IServiceCollection services, CadDesktopHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddViewModels();
        services.AddCadMessagePipe();
        services.AddSingleton(CadRenderResourceBudget.Shared);
        services.AddSingleton<ICadRenderSessionFactory, Direct2DRenderSessionFactory>();

        var settings = options.Settings;
        // Launch-only MCP values (--mcp-port, --mcp-token, the stdio bridge) must not reach the saved file.
        services.AddSingleton<IUserSettingsStore>(_ => new SessionMcpUserSettingsStore(
            new JsonUserSettingsStore(settings.GetPath("user-settings.json"), options.UserSettingsFallbackPath)));
        services.AddSingleton<IWorkspaceSettingsStore>(_ => new JsonWorkspaceSettingsStore(settings.GetPath("workspace-settings.json")));
        services.AddSingleton<IToolboxLayoutSettingsStore>(_ => new JsonToolboxLayoutSettingsStore(settings.GetPath("toolbox-settings.json")));
        services.AddSingleton<IAiAssistantSettingsStore>(_ => new JsonAiAssistantSettingsStore(options.AiSettingsPath));

        services.AddSingleton<ICadMessageLog, CadMessageLog>();
        services.AddSingleton<ICadCommandLineService, CadCommandLineService>();
        services.AddSingleton<ICadMcpService>(sp => CreateMcpService(sp, options.CreateUiSynchronizationContext()));

        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IAiChatClient, LmStudioChatClient>();
        services.AddSingleton<ICodexAgentClient, CodexAppServerClient>();
        return services;
    }

    /// <summary>Starts MCP as the command line asks (--mcp-port, --mcp-token); failures go to the message log.</summary>
    public static async Task StartMcpAsync(IServiceProvider services, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            var args = arguments as string[] ?? [.. arguments];
            await services.GetRequiredService<MainViewModel>().InitializeMcpAsync(
                CadMcpServer.GetRequestedPort(args),
                CadMcpServer.GetRequestedAccessToken(args));
        }
        catch (Exception exception)
        {
            services.GetRequiredService<ICadMessageLog>().Add(exception.Message, CadMessageLevel.Error, "MCP");
        }
    }

    /// <summary>Stops MCP before the container, so its listener does not outlive the services it calls.</summary>
    public static void DisposeServices(ServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        (services.GetService<ICadMcpService>() as IDisposable)?.Dispose();
        services.Dispose();
    }

    private static CadMcpService CreateMcpService(IServiceProvider sp, SynchronizationContext uiContext)
    {
        var service = new CadMcpService(uiContext,
            sp.GetRequiredService<ICadToolWorkspace>(), sp.GetService<IImageImportService>(),
            sp.GetService<IAsyncPublisher<CadToolActivityMessage>>());
        var log = sp.GetRequiredService<ICadMessageLog>();
        service.StatusChanged += (_, _) =>
        {
            if (service.Status is { State: CadMcpState.Running, Endpoint: { } endpoint })
                log.Add($"MCP listening at {endpoint}", source: "MCP");
            else if (service.Status is { State: CadMcpState.Failed, Error: { } error })
                log.Add(error, CadMessageLevel.Error, "MCP");
        };
        return service;
    }
}
