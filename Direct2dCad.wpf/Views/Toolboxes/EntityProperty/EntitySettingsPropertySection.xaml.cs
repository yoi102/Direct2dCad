using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class EntitySettingsPropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IEntitySettingsPropertySectionViewModel),
        typeof(EntitySettingsPropertySection));

    public IEntitySettingsPropertySectionViewModel? ViewModel
    {
        get => (IEntitySettingsPropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public EntitySettingsPropertySection()
    {
        InitializeComponent();
    }
}
