using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<CadDocumentViewModel>();

        return services;
    }
}
