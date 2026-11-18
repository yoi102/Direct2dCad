using Direct2dCad.Application.Tools;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Services.Events;

/// <summary>
/// MessagePipe registration shared by the desktop hosts. Every message type is registered as a closed generic:
/// open-generic resolution is unavailable under NativeAOT (the Avalonia host), so a type missing from this list would
/// only fail there, at runtime. <c>CadMessagePipeTests</c> checks the list against every publisher and subscriber.
/// </summary>
public static class CadMessagePipe
{
    public static IReadOnlyList<Type> MessageTypes { get; } =
    [
        typeof(CadDocumentInteractionStateChangedMessage),
        typeof(CadDocumentViewSettingsChangedMessage),
        typeof(CadSelectionFilterChangedMessage),
        typeof(CadOleObjectUpdatedMessage),
        typeof(CadBlockDefinitionSelectionChangedMessage),
        typeof(EditorTabDocumentSummaryChangedMessage),
        typeof(ThemeChangedEvent),
    ];

    public static IReadOnlyList<Type> AsyncMessageTypes { get; } =
    [
        typeof(CadCommandActivityMessage),
        typeof(CadInteractionActivityMessage),
        typeof(CadToolActivityMessage),
    ];

    public static IServiceCollection AddCadMessagePipe(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMessagePipe(options => options.EnableAutoRegistration = false);
        // Generic calls (not MakeGenericType over MessageTypes) keep the closed types visible to the AOT compiler.
        Add<CadDocumentInteractionStateChangedMessage>(services);
        Add<CadDocumentViewSettingsChangedMessage>(services);
        Add<CadSelectionFilterChangedMessage>(services);
        Add<CadOleObjectUpdatedMessage>(services);
        Add<CadBlockDefinitionSelectionChangedMessage>(services);
        Add<EditorTabDocumentSummaryChangedMessage>(services);
        Add<ThemeChangedEvent>(services);
        AddAsync<CadCommandActivityMessage>(services);
        AddAsync<CadInteractionActivityMessage>(services);
        AddAsync<CadToolActivityMessage>(services);
        return services;
    }

    private static void Add<T>(IServiceCollection services)
    {
        services.AddSingleton<MessageBrokerCore<T>>();
        services.AddSingleton<MessageBroker<T>>();
        services.AddSingleton<IPublisher<T>>(sp => sp.GetRequiredService<MessageBroker<T>>());
        services.AddSingleton<ISubscriber<T>>(sp => sp.GetRequiredService<MessageBroker<T>>());
    }

    private static void AddAsync<T>(IServiceCollection services)
    {
        services.AddSingleton<AsyncMessageBrokerCore<T>>();
        services.AddSingleton<AsyncMessageBroker<T>>();
        services.AddSingleton<IAsyncPublisher<T>>(sp => sp.GetRequiredService<AsyncMessageBroker<T>>());
        services.AddSingleton<IAsyncSubscriber<T>>(sp => sp.GetRequiredService<AsyncMessageBroker<T>>());
    }
}
