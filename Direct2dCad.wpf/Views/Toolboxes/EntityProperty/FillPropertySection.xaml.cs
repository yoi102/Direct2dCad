using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class FillPropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IFillPropertySectionViewModel),
        typeof(FillPropertySection));

    public IFillPropertySectionViewModel? ViewModel
    {
        get => (IFillPropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public FillPropertySection()
    {
        InitializeComponent();
    }
}
