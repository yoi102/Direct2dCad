using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
namespace Direct2dCad.Avalonia.Views.Toolboxes;

public partial class LayersView : UserControl
{
    private static readonly DataFormat<LayerItemViewModel> LayerFormat = DataFormat.CreateInProcessFormat<LayerItemViewModel>("Direct2dCad.Layer");
    private PointerPressedEventArgs? _pressed;
    private Point _origin;
    private LayerItemViewModel? _layer;
    public LayersView()
    {
        InitializeComponent(); AttachLayerMenu(); DragDrop.SetAllowDrop(LayersList, true);
        LayersList.AddHandler(DragDrop.DragOverEvent, (_, e) => { if (!e.DataTransfer.Contains(LayerFormat)) return; e.DragEffects = DragDropEffects.Move; e.Handled = true; });
        LayersList.AddHandler(DragDrop.DropEvent, (_, e) => {
            if (DataContext is not LayersToolboxViewModel vm || e.DataTransfer.TryGetValue(LayerFormat) is not { } layer) return;
            var point = e.GetPosition(LayersList); var index = vm.Layers.Count;
            foreach (var item in LayersList.GetVisualDescendants().OfType<ListBoxItem>())
                if (item.DataContext is LayerItemViewModel target && item.TranslatePoint(new Point(), LayersList) is { } position && point.Y < position.Y + item.Bounds.Height / 2) { index = vm.Layers.IndexOf(target); break; }
            vm.MoveLayer(layer, index); e.Handled = true;
        });
    }
    private void AttachLayerMenu()
    {
        var menu = new ContextMenu();
        var add = CadListContextMenu.Item(menu,"AddLayer");
        var delete = CadListContextMenu.Item(menu,"DeleteLayer");
        menu.Items.Add(new Separator());
        MenuItem State(string key, Func<LayerItemViewModel,bool> get, Action<LayerItemViewModel,bool> set)
        {
            var item = CadListContextMenu.Item(menu,key); item.ToggleType=MenuItemToggleType.CheckBox;
            item.Command = new RelayCommand(() => { if(DataContext is LayersToolboxViewModel {SelectedLayer:{} layer} vm && vm.Layers.Contains(layer))set(layer,!get(layer)); });
            return item;
        }
        var visible=State("Visible",layer=>layer.IsVisible,(layer,value)=>layer.IsVisible=value);
        var locked=State("Locked",layer=>layer.IsLocked,(layer,value)=>layer.IsLocked=value);
        var frozen=State("Frozen",layer=>layer.IsFrozen,(layer,value)=>layer.IsFrozen=value);
        menu.Items.Add(new Separator());
        var up=CadListContextMenu.Item(menu,"MoveUp");
        var down=CadListContextMenu.Item(menu,"MoveDown");
        var refresh=CadListContextMenu.Item(menu,"Refresh");
        CadListContextMenu.Attach(LayersList,menu,selected=>
        {
            var vm=DataContext as LayersToolboxViewModel;
            var layer=selected as LayerItemViewModel;
            CadListContextMenu.Target(add,vm?.AddLayerCommand);
            CadListContextMenu.Target(delete,vm?.DeleteSelectedLayerCommand);
            CadListContextMenu.Target(up,vm?.MoveSelectedLayerUpCommand);
            CadListContextMenu.Target(down,vm?.MoveSelectedLayerDownCommand);
            CadListContextMenu.Target(refresh,vm?.RefreshCommand);
            foreach(var item in new[]{visible,locked,frozen})item.IsEnabled=vm?.HasDocument==true && layer is not null && vm.Layers.Contains(layer);
            visible.IsChecked=layer?.IsVisible==true;locked.IsChecked=layer?.IsLocked==true;frozen.IsChecked=layer?.IsFrozen==true;
        },this);
    }
    private void HandlePressed(object? sender, PointerPressedEventArgs e) { if (sender is Control { DataContext: LayerItemViewModel layer } control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) { _pressed = e; _origin = e.GetPosition(this); _layer = layer; } }
    private void HandleReleased(object? sender, PointerReleasedEventArgs e) => _pressed = null;
    private async void HandleMoved(object? sender, PointerEventArgs e)
    {
        var delta = e.GetPosition(this) - _origin;
        if (_pressed is null || _layer is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Math.Abs(delta.X) + Math.Abs(delta.Y) < 6) return;
        var start = _pressed; _pressed = null; var data = new DataTransfer(); var item = new DataTransferItem(); item.Set(LayerFormat, _layer); data.Add(item); await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
    }
}
