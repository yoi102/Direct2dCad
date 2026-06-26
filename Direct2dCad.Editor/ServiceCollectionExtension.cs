using Direct2dCad.Db.Cad;
using Direct2dCad.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Editor;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddDirect2dCadEditor(this IServiceCollection services)
    {
        services.AddTransient<ICadSpatialIndex, CadSpatialIndex>();
        services.AddTransient(static _ => CadDocument.Create("Untitled"));
        services.AddTransient<CadEditor>();
        services.AddTransient<CadSession>();

        return services;
    }
}
