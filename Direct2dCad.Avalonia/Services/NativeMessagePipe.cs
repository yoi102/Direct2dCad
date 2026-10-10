using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.Application.Tools;
namespace Direct2dCad.Avalonia.Services;

internal static class NativeMessagePipe
{
    public static void Register(IServiceCollection services)
    {
        services.AddMessagePipe(options => options.EnableAutoRegistration = false);
        Add<CadDocumentInteractionStateChangedMessage>(services); Add<CadDocumentViewSettingsChangedMessage>(services);
        Add<CadSelectionFilterChangedMessage>(services); Add<CadOleObjectUpdatedMessage>(services);
        Add<CadBlockDefinitionSelectionChangedMessage>(services); Add<EditorTabDocumentSummaryChangedMessage>(services);
        AddAsync<CadCommandActivityMessage>(services); AddAsync<CadInteractionActivityMessage>(services); AddAsync<CadToolActivityMessage>(services);
    }
    private static void Add<T>(IServiceCollection services)
    {
        services.AddSingleton<MessageBrokerCore<T>>(); services.AddSingleton<MessageBroker<T>>();
        services.AddSingleton<IPublisher<T>>(sp => sp.GetRequiredService<MessageBroker<T>>());
        services.AddSingleton<ISubscriber<T>>(sp => sp.GetRequiredService<MessageBroker<T>>());
    }
    private static void AddAsync<T>(IServiceCollection services)
    {
        services.AddSingleton<AsyncMessageBrokerCore<T>>(); services.AddSingleton<AsyncMessageBroker<T>>();
        services.AddSingleton<IAsyncPublisher<T>>(sp => sp.GetRequiredService<AsyncMessageBroker<T>>());
        services.AddSingleton<IAsyncSubscriber<T>>(sp => sp.GetRequiredService<AsyncMessageBroker<T>>());
    }
}
