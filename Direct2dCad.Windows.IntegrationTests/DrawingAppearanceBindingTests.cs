using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;
using MahApps.Metro.Controls;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class DrawingAppearanceBindingTests
{
    [Fact]
    public void ColorSourceControlsAllowChangingFromLayerToExplicitColor()
    {
        RunSta(() =>
        {
            // MahApps shares its recent-color palette; keep both cases on one UI Dispatcher.
            foreach (var hasSourceSelector in new[] { false, true })
                VerifyColorSourceControls(hasSourceSelector);
        });
    }

    private static void VerifyColorSourceControls(bool hasSourceSelector)
    {
        var model = new AppearanceModel(hasSourceSelector);
        var view = new StrokeAppearancePropertySection { ViewModel = model };
        view.Measure(new Size(400, 800));
        view.Arrange(new Rect(0, 0, 400, 800));
        FlushBindings();
        var controls = Descendants(view).ToArray();
        var byLayer = controls.OfType<CheckBox>().Single(control =>
            BindingOperations.GetBinding(control, CheckBox.IsCheckedProperty)?.Path.Path == "ViewModel.UseByLayerColor");
        var sources = controls.OfType<ComboBox>().Single(control =>
            BindingOperations.GetBinding(control, ItemsControl.ItemsSourceProperty)?.Path.Path == "ViewModel.ColorSourceOptions");
        var picker = Assert.Single(controls.OfType<ColorPicker>());
        Assert.Equal(hasSourceSelector ? Visibility.Collapsed : Visibility.Visible, byLayer.Visibility);
        Assert.Equal(hasSourceSelector ? Visibility.Visible : Visibility.Collapsed, sources.Visibility);
        Assert.False(picker.IsEnabled);

        if (hasSourceSelector)
            sources.SetCurrentValue(ComboBox.SelectedItemProperty, model.ColorSourceOptions[1]);
        else
            byLayer.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        FlushBindings();
        Assert.True(picker.IsEnabled);
        if (!hasSourceSelector) Assert.False(model.UseByLayerColor);
        picker.SetCurrentValue(ColorPicker.SelectedColorProperty, System.Windows.Media.Colors.Red);
        FlushBindings();
        Assert.Equal(CadColor.Red, model.StrokeColor);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class AppearanceModel(bool hasSourceSelector) : ObservableObject, IStrokeAppearancePropertySectionViewModel
    {
        private bool _useByLayerColor = true;
        private EntityColorSourceOption? _selectedColorSourceOption;
        public CadColor StrokeColor { get; set; } = CadColor.Green;
        public bool UseByLayerColor
        {
            get => _useByLayerColor;
            set { SetProperty(ref _useByLayerColor, value); OnPropertyChanged(nameof(ColorControlsEnabled)); }
        }
        public bool ColorControlsEnabled => !UseByLayerColor;
        public bool SupportsColorSourceSelection => hasSourceSelector;
        public bool IsExplicitColorSource => SelectedColorSourceOption?.Value == CadColorSource.Explicit;
        public IReadOnlyList<EntityColorSourceOption> ColorSourceOptions { get; } =
            [new(CadColorSource.ByLayer, "By layer"), new(CadColorSource.Explicit, "Explicit")];
        public EntityColorSourceOption? SelectedColorSourceOption
        {
            get => _selectedColorSourceOption ?? ColorSourceOptions[0];
            set { SetProperty(ref _selectedColorSourceOption, value); OnPropertyChanged(nameof(IsExplicitColorSource)); }
        }
        public double LineWeight { get; set; } = 1;
        public bool UseByLayerLineWeight { get; set; } = true;
        public bool LineWeightControlsEnabled => !UseByLayerLineWeight;
    }
}
