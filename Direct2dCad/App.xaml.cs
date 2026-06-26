using CommunityToolkit.Mvvm.DependencyInjection;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{

    public App()
    {
        var serviceProvider = new ServiceCollection();
        serviceProvider.AddDirect2dCadEditor()
                       .AddViewModels()
                       .AddMessagePipe();

        Ioc.Default.ConfigureServices(serviceProvider.BuildServiceProvider());
    }












}

