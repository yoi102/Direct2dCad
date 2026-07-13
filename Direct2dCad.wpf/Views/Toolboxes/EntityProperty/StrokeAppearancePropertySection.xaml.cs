using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class StrokeAppearancePropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IStrokeAppearancePropertySectionViewModel),
        typeof(StrokeAppearancePropertySection));

    public IStrokeAppearancePropertySectionViewModel? ViewModel
    {
        get => (IStrokeAppearancePropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public StrokeAppearancePropertySection()
    {
        InitializeComponent();
    }
}
