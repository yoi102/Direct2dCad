using Direct2dCad.ViewServices.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.wpf.ViewServices;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddViewServices(this IServiceCollection services)
    {
        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddTransient<IMessageBoxService, MessageBoxService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        return services;
    }

    [Obsolete("Use AddViewServices instead.")]
    public static IServiceCollection AddDialogService(this IServiceCollection services)
    {
        return services.AddViewServices();
    }
}
