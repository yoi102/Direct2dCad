using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class EntityHeaderPropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IEntityHeaderPropertySectionViewModel),
        typeof(EntityHeaderPropertySection));

    public IEntityHeaderPropertySectionViewModel? ViewModel
    {
        get => (IEntityHeaderPropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public EntityHeaderPropertySection()
    {
        InitializeComponent();
    }
}
