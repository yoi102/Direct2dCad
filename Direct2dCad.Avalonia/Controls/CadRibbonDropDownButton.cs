using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Direct2dCad.Avalonia.Controls;

public sealed class CadRibbonDropDownButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public CadRibbonDropDownButton()
    {
        ContextRequested += (_,e) =>
        {
            if (!e.Handled && Flyout is MenuFlyout menu) {menu.ShowAt(this, true);e.Handled=true;}
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && Flyout is MenuFlyout menu)
        {
            menu.ShowAt(this);
            Dispatcher.UIThread.Post(() => menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEffectivelyEnabled && item.IsEffectivelyVisible)?.Focus(), DispatcherPriority.Loaded);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
