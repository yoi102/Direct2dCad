using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class DrawingLayerPropertySection : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IDrawingLayerPropertySectionViewModel),
        typeof(DrawingLayerPropertySection));

    public IDrawingLayerPropertySectionViewModel? ViewModel
    {
        get => (IDrawingLayerPropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public DrawingLayerPropertySection()
    {
        InitializeComponent();
    }
}
