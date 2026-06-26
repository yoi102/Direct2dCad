using Direct2dCad.IDialogService;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.wpf.DialogService;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddDialogService(this IServiceCollection services)
    {
        services.AddTransient<IFileDialogService, FileDialogService>();
        services.AddTransient<IMessageBoxService, MessageBoxService>();
        return services;
    }
}
