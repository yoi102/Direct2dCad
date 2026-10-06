using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class PropertySaveBindingTests
{
    [Fact]
    public void SaveCommitsThePropertyNameBeforeFocusLeavesTheActualEditor()
    {
        RunSta(() =>
        {
            var model = new PropertyModel();
            var section = new EntityHeaderPropertySection { ViewModel = model };
            var textBox = Descendants(section).OfType<TextBox>().Single(control => !control.IsReadOnly);
            FlushBindings();
            textBox.SetCurrentValue(TextBox.TextProperty, "Changed before saving");
            Assert.Equal("Original", model.EntityName);

            Assert.True(MainWindow.TryCommitFocusedPropertyEdit(textBox));

            Assert.Equal("Changed before saving", model.EntityName);
            Assert.Equal("Changed before saving", textBox.Text);
        });
    }

    [Fact]
    public void InvalidPropertyTextBlocksSavingAndSurvivesUntilCorrected()
    {
        RunSta(() =>
        {
            var model = new PropertyModel();
            var section = new EntitySettingsPropertySection { ViewModel = model };
            var textBox = Descendants(section).OfType<TextBox>().Single();
            FlushBindings();
            textBox.SetCurrentValue(TextBox.TextProperty, "invalid angle");

            Assert.False(MainWindow.TryCommitFocusedPropertyEdit(textBox));
            Assert.Equal(12, model.GeometryRotationDegrees);
            Assert.Equal("invalid angle", textBox.Text);
            Assert.True(Validation.GetHasError(textBox));

            textBox.SetCurrentValue(TextBox.TextProperty, "27");
            Assert.True(MainWindow.TryCommitFocusedPropertyEdit(textBox));
            Assert.Equal(27, model.GeometryRotationDegrees);
        });
    }

    [Fact]
    public void SaveDoesNotCommitANonPropertyDraft()
    {
        RunSta(() =>
        {
            var model = new PropertyModel();
            var draft = new TextBox { DataContext = model };
            draft.SetBinding(TextBox.TextProperty, new Binding(nameof(PropertyModel.EntityName))
                { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            _ = new UserControl { Content = draft };
            FlushBindings();
            draft.SetCurrentValue(TextBox.TextProperty, "Unsubmitted terminal draft");

            Assert.True(MainWindow.TryCommitFocusedPropertyEdit(draft));

            Assert.Equal("Original", model.EntityName);
            Assert.Equal("Unsubmitted terminal draft", draft.Text);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF save binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class PropertyModel : IEntityHeaderPropertySectionViewModel, IEntitySettingsPropertySectionViewModel
    {
        public string EntityIdText => "1";
        public string EntityName { get; set; } = "Original";
        public IReadOnlyList<EntityLayerOption> LayerOptions => [];
        public EntityLayerOption? SelectedLayerOption { get; set; }
        public int ZIndex { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool SupportsGeometryOrientation => true;
        public double GeometryRotationDegrees { get; set; } = 12;
    }
}
