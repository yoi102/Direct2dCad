using System.Windows.Controls;
using System.Windows.Input;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class DrawingRecoveryToolboxView : UserControl
{
    public DrawingRecoveryToolboxView() => InitializeComponent();

    private void RecoveryEntryPreviewMouseDown(object sender, MouseButtonEventArgs e) => SelectRecoveryEntry(sender);

    private void RecoveryEntryGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => SelectRecoveryEntry(sender);

    private static void SelectRecoveryEntry(object sender)
    {
        if (sender is ListBoxItem item)
            item.SetCurrentValue(ListBoxItem.IsSelectedProperty, true);
    }
}
