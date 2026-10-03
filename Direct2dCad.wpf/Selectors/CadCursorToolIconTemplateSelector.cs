using System.Windows;
using System.Windows.Controls;

namespace Direct2dCad.wpf.Selectors;

public sealed class CadCursorToolIconTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        (container as FrameworkElement)?.TryFindResource($"Cad{item}IconTemplate") as DataTemplate;
}
