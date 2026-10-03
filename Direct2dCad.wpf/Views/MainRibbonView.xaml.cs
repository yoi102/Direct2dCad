using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Direct2dCad.wpf.Views;

/// <summary>
/// MainRibbonView.xaml 的交互逻辑
/// </summary>
public partial class MainRibbonView : UserControl
{
    public MainRibbonView()
    {
        InitializeComponent();
    }

    private void RibbonDropDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void RibbonDropDownButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && sender is Button { ContextMenu: { } menu })
        {
            RibbonDropDownButton_OnClick(sender, e);
            menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)?.Focus();
            e.Handled = true;
        }
    }
}
