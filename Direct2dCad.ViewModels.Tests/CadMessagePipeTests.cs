using System.Reflection;
using Direct2dCad.Application.Tools;
using Direct2dCad.ViewModels.Services.Events;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadMessagePipeTests
{
    private static readonly Type[] SyncContracts = [typeof(IPublisher<>), typeof(ISubscriber<>)];
    private static readonly Type[] AsyncContracts = [typeof(IAsyncPublisher<>), typeof(IAsyncSubscriber<>)];

    [Fact]
    public void EveryPublishedOrSubscribedMessageTypeIsRegistered()
    {
        // The Avalonia host runs under NativeAOT, where an unregistered message type only fails at runtime.
        Assembly[] assemblies =
        [
            typeof(MainViewModel).Assembly,
            typeof(CadMessagePipe).Assembly,
            typeof(CadToolActivityMessage).Assembly,
        ];
        var used = assemblies.SelectMany(GetTypes).SelectMany(UsedContracts).ToHashSet();

        var missing = used
            .Where(use => !(SyncContracts.Contains(use.Contract) ? CadMessagePipe.MessageTypes : CadMessagePipe.AsyncMessageTypes)
                .Contains(use.Message))
            .Select(use => $"{use.Contract.Name}<{use.Message.Name}>")
            .ToArray();

        Assert.Empty(missing);
        Assert.NotEmpty(used);
    }

    [Fact]
    public void ListedMessageTypesResolveAsClosedRegistrations()
    {
        var services = new ServiceCollection().AddCadMessagePipe();
        var closed = services.Select(descriptor => descriptor.ServiceType).Where(type => !type.ContainsGenericParameters).ToHashSet();

        foreach (var message in CadMessagePipe.MessageTypes)
            Assert.All(SyncContracts, contract => Assert.Contains(contract.MakeGenericType(message), closed));
        foreach (var message in CadMessagePipe.AsyncMessageTypes)
            Assert.All(AsyncContracts, contract => Assert.Contains(contract.MakeGenericType(message), closed));

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPublisher<CadSelectionFilterChangedMessage>>());
        Assert.NotNull(provider.GetRequiredService<IAsyncSubscriber<CadToolActivityMessage>>());
    }

    private static IEnumerable<Type> GetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }

    private static IEnumerable<(Type Contract, Type Message)> UsedContracts(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var candidates = type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
            .Concat(type.GetFields(all).Select(f => f.FieldType));
        foreach (var candidate in candidates)
        {
            if (!candidate.IsGenericType || candidate.ContainsGenericParameters)
                continue;
            var definition = candidate.GetGenericTypeDefinition();
            if (SyncContracts.Contains(definition) || AsyncContracts.Contains(definition))
                yield return (definition, candidate.GetGenericArguments()[0]);
        }
    }
}
