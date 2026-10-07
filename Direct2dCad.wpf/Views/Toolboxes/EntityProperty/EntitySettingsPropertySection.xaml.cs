using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class EntitySettingsPropertySection : UserControl
{
    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(EntitySettingsPropertySection));

    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public static readonly DependencyProperty EntityIdTextProperty = DependencyProperty.Register(
        nameof(EntityIdText), typeof(string), typeof(EntitySettingsPropertySection), new PropertyMetadata(string.Empty));

    public string EntityIdText
    {
        get => (string)GetValue(EntityIdTextProperty);
        set => SetValue(EntityIdTextProperty, value);
    }

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
