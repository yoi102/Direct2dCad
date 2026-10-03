using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Direct2dCad.wpf.Views.Toolboxes;
/// <summary>
/// EntityPropertiesToolboxView.xaml 的交互逻辑
/// </summary>
public partial class EntityPropertiesToolboxView : UserControl
{
    public EntityPropertiesToolboxView()
    {
        InitializeComponent();
    }

    private void PropertiesScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer outer || outer.ScrollableHeight <= 0)
            return;

        for (var source = e.OriginalSource as DependencyObject; source is not null;
             source = source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is not ScrollViewer inner)
                continue;

            // Entity views have their own scroll viewers. In this panel they expand
            // to their full height, so pass their wheel input to the shared viewer.
            if (!ReferenceEquals(inner, outer) && inner.ScrollableHeight <= 0)
            {
                var distance = SystemParameters.WheelScrollLines < 0
                    ? outer.ViewportHeight
                    : SystemParameters.WheelScrollLines * 16;
                outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta / 120d * distance);
                e.Handled = true;
            }
            return;
        }
    }
}
