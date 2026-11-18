using System.Windows.Threading;
using Direct2dCad.wpf;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class WpfCompositionTests
{
    [Fact]
    public void EveryRegistrationOfTheWpfHostCanBeConstructed()
    {
        // ValidateOnBuild checks each service's constructor dependencies without creating windows or reading settings,
        // so a registration dropped from the shared Direct2dCad.Desktop composition fails here instead of at startup.
        var services = new ServiceCollection();
        App.ConfigureServices(services, Dispatcher.CurrentDispatcher);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(MainWindow));
    }
}
