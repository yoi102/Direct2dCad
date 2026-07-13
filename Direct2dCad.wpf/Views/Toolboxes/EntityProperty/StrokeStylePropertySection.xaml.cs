using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class StrokeStylePropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IStrokeStylePropertySectionViewModel),
        typeof(StrokeStylePropertySection));

    public IStrokeStylePropertySectionViewModel? ViewModel
    {
        get => (IStrokeStylePropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public StrokeStylePropertySection()
    {
        InitializeComponent();
    }
}
