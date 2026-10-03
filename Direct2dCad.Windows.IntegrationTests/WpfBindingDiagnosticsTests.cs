using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Direct2dCad.wpf.Services.Application;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection(WpfBindingDiagnosticsCollection.Name)]
public sealed class WpfBindingDiagnosticsTests
{
    [Fact]
    public void DetachedDockButtonsAndTabVisibilityFollowTheirModelsInBothThemes()
    {
        RunSta(() =>
        {
            // Exercise model updates without a visual parent. Complete-window trace cleanliness
            // is checked by RecoveryToolboxUiTests, including the application resource scope.
            foreach (var dark in new[] { true, false })
            {
                var manager = new ToggleDockingManager { ButtonSize = 28 };
                var pane = new LayoutAnchorablePane();
                manager.Layout.RootPanel.Children.Add(new LayoutAnchorablePaneGroup(pane));
                var first = new LayoutAnchorable { Title = "First" };
                pane.Children.Add(first);
                var theme = new CadDockTheme(dark);
                manager.Theme = theme;
                var button = new ToggleDockButton
                {
                    Style = (Style)theme.ThemeResourceDictionary[typeof(ToggleDockButton)],
                    Anchorable = first
                };
                var paneStyle = (Style)theme.ThemeResourceDictionary["ToggleAnchorablePaneControlStyle"];
                var tabStyle = (Style)paneStyle.Setters.OfType<Setter>().Single(setter =>
                    setter.Property == ItemsControl.ItemContainerStyleProperty).Value;
                var tab = new TabItem { DataContext = first, Style = tabStyle };
                FlushBindings();
                Assert.Equal(Visibility.Collapsed, tab.Visibility);
                Assert.Equal(28, button.Width);

                pane.Children.Add(new LayoutAnchorable { Title = "Second" });
                manager.ButtonSize = 32;
                FlushBindings();
                Assert.Equal(Visibility.Visible, tab.Visibility);
                Assert.Equal(32, button.Width);
                Assert.Equal(32, button.MinHeight);
                pane.Children.RemoveAt(1);
                FlushBindings();
                Assert.Equal(Visibility.Collapsed, tab.Visibility);
            }
        });
    }

    [Fact]
    public void DiagnosticsCaptureAnActualInvalidBindingWithoutADebugger()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Direct2dCad-BindingProbe-{Guid.NewGuid():N}.log");
        try
        {
            RunSta(() =>
            {
                using var diagnostics = WpfBindingDiagnostics.Start(path);
                var text = new TextBlock { DataContext = new object() };
                text.SetBinding(TextBlock.TextProperty, new Binding("MissingBindingDiagnosticsProbe"));
                FlushBindings();
            });
            Assert.Contains("MissingBindingDiagnosticsProbe", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfBindingDiagnosticsCollection
{
    public const string Name = "WPF binding diagnostics";
}
