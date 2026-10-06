using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels;

/// <summary>Creates a document whose service scope is released when its tab is disposed.</summary>
public interface IEditorTabFactory
{
    EditorTabViewModel Create(Action<EditorTabViewModel>? initialize = null);
}

internal sealed class EditorTabFactory(IServiceScopeFactory scopeFactory) : IEditorTabFactory
{
    public EditorTabViewModel Create(Action<EditorTabViewModel>? initialize = null)
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var tab = scope.ServiceProvider.GetRequiredService<EditorTabViewModel>();
            tab.OwnDocumentScope(scope);
            initialize?.Invoke(tab);
            return tab;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
