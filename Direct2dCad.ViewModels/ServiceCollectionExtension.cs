using Direct2dCad.Agent;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<EditorTabViewModel>();
        services.AddTransient<CadDocumentViewModel>();
        services.AddSingleton<ICadClipboardStore, CadClipboardStore>();
        services.AddSingleton<IActiveEditorContext, ActiveEditorContext>();
        services.AddSingleton<ICadToolWorkspace, CadToolWorkspace>();
        services.AddSingleton<ICadToolCommandLineService, CadToolCommandLineService>();
        services.AddSingleton<IAgentRunner, AgentRunner>();

        return services;
    }
}
