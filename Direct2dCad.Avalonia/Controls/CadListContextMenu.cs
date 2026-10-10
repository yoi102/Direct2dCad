using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Direct2dCad.Avalonia.Controls;

internal static class CadListContextMenu
{
    public static void Attach(ListBox list, ContextMenu menu, Action<object?> prepare, Control? host = null)
    {
        (host ?? list).ContextMenu = menu;
        // Select the row under a right press before the menu opens. The container
        // includes its padding; blank list space keeps the current selection.
        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(list).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed || e.Source is not Visual source) return;
            var row = source.GetVisualAncestors().Prepend(source).OfType<ListBoxItem>().FirstOrDefault();
            if (row is not null && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), list))
                list.SelectedItem = row.DataContext;
        }, RoutingStrategies.Tunnel);
        menu.Opening += (_, _) => prepare(list.SelectedItem);
    }

    public static MenuItem Item(ContextMenu menu, string key)
    {
        var item = new MenuItem();
        Loc.Track(item, MenuItem.HeaderProperty, key);
        menu.Items.Add(item);
        return item;
    }

    public static void Target(MenuItem item, System.Windows.Input.ICommand? command, object? parameter = null)
    {
        item.Command = command;
        item.CommandParameter = parameter;
        item.IsEnabled = command?.CanExecute(parameter) == true;
    }
}
