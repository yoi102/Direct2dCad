using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Direct2dCad.wpf.Views;

/// <summary>
/// MainStatusBarView.xaml 的交互逻辑
/// </summary>
public partial class MainStatusBarView : UserControl
{
    public MainStatusBarView()
    {
        InitializeComponent();
    }

    private void ViewSettingsPopup_OnLoaded(object sender, RoutedEventArgs e)
    {
        // PopupBox exposes its interactive surface through the template toggle.
        ViewSettingsPopup.ApplyTemplate();
        if (ViewSettingsPopup.Template.FindName("PART_Toggle", ViewSettingsPopup) is ToggleButton toggle)
        {
            toggle.SetResourceReference(StyleProperty, "StatusIconToggle");
            toggle.Margin = new Thickness(0);
            AutomationProperties.SetAutomationId(toggle, AutomationProperties.GetAutomationId(ViewSettingsPopup));
            BindingOperations.SetBinding(toggle, AutomationProperties.NameProperty,
                new Binding { Source = ViewSettingsPopup, Path = new PropertyPath(AutomationProperties.NameProperty) });
        }
    }

    private void ViewSettingsPopup_OnOpened(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ViewSettingsPopup.IsPopupOpen)
            {
                PolarAngleInput.Focus();
                PolarAngleInput.SelectAll();
            }
        }));
    }

    private void ViewSettingsPanel_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource == PolarAngleInput)
        {
            PolarAngleInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewSettingsPopup.IsPopupOpen = false;
            ViewSettingsPopup.Focus();
            e.Handled = true;
        }
    }

    private void ViewSettingsPopup_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource == ViewSettingsPopup && e.Key is Key.Space or Key.Enter)
        {
            ViewSettingsPopup.IsPopupOpen = !ViewSettingsPopup.IsPopupOpen;
            e.Handled = true;
        }
    }
}
