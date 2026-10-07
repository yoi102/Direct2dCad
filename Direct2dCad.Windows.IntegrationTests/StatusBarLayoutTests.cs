using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using Direct2dCad.wpf.Views;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection(WpfBindingDiagnosticsCollection.Name)]
public sealed class StatusBarLayoutTests
{
    [Fact]
    public void ActiveDrawingStepAndStatusControlsReceiveVisibleLayoutSpace()
    {
        WpfTestDispatcher.Run(() =>
        {
            var application = System.Windows.Application.Current!;
            const string buttonStyleKey = "MaterialDesignFlatButton";
            var hadButtonStyle = application.Resources.Contains(buttonStyleKey);
            var previousButtonStyle = application.Resources[buttonStyleKey];
            try
            {
                // The complete view resolves its base button style at construction.
                // Supply only that resource for this test in the shared Application;
                // no window, keyboard focus or native input is created.
                application.Resources[buttonStyleKey] = new Style(typeof(Button));
                var status = new MainStatusBarView
                {
                    DataContext = new
                    {
                        CurrentEditorTabViewModel = new
                        {
                            CadDocumentViewModel = new
                            {
                                HasActiveDrawingTool = true,
                                IsGripEditing = false,
                                IsPastePreviewActive = false,
                                CurrentToolNameDisplay = "Polyline",
                                CurrentStepPrompt = "Specify next point",
                                StepInputError = "Specify 1 more point."
                            }
                        }
                    }
                };
                var layout = new Grid();
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.Children.Add(new Border { Height = 150 });
                var canvas = new Border();
                Grid.SetRow(canvas, 1);
                layout.Children.Add(canvas);
                Grid.SetRow(status, 2);
                layout.Children.Add(status);
                layout.Measure(new Size(800, 600));
                layout.Arrange(new Rect(0, 0, 800, 600));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                layout.UpdateLayout();

                Assert.InRange(status.ActualHeight, 45, 64);
                var step = Assert.Single(Descendants(status).OfType<TextBlock>(), element =>
                    AutomationProperties.GetAutomationId(element) == "CurrentDrawingStepText");
                Assert.Equal(Visibility.Visible, step.Visibility);
                Assert.True(step.ActualHeight > 0);
                Assert.Contains("Specify 1 more point", string.Concat(step.Inlines.OfType<Run>().Select(run => run.Text)));
                var bindings = step.Inlines.OfType<Run>()
                    .Select(run => BindingOperations.GetBindingExpression(run, Run.TextProperty))
                    .Where(binding => binding is not null).ToArray();
                Assert.Equal(2, bindings.Length);
                Assert.All(bindings, binding => Assert.False(binding!.HasError));
                Assert.Contains("Specify 1 more point", UIElementAutomationPeer.CreatePeerForElement(step)!.GetName());
                var tool = Assert.Single(Descendants(status).OfType<TextBlock>(), element =>
                    AutomationProperties.GetAutomationId(element) == "CurrentToolStatusText");
                Assert.True(tool.ActualHeight > 0);
                Assert.Equal("Polyline", tool.Text);
            }
            finally
            {
                if (hadButtonStyle) application.Resources[buttonStyleKey] = previousButtonStyle;
                else application.Resources.Remove(buttonStyleKey);
            }
        });
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
