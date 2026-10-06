using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;
using MahApps.Metro.Controls;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class PropertyEditorAccessibilityTests
{
    [Fact]
    public void CoordinateEditorsExposeDistinctNamesThroughTheirAutomationPeers()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var view in new UserControl[] { new LinePropertyView(), new CirclePropertyView(), new RectanglePropertyView() })
                {
                    var numericEditors = Descendants(view).OfType<NumericUpDown>().ToArray();
                    Assert.NotEmpty(numericEditors);
                    foreach (var editor in numericEditors)
                    {
                        var expectedName = AutomationProperties.GetName(editor);
                        Assert.False(string.IsNullOrWhiteSpace(expectedName));
                        var peer = UIElementAutomationPeer.CreatePeerForElement(editor);
                        Assert.NotNull(peer);
                        Assert.Equal(expectedName, peer.GetName());
                    }
                    Assert.Equal(numericEditors.Length,
                        numericEditors.Select(AutomationProperties.GetName).Distinct().Count());
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF accessibility test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
