using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes;
using GongSolutions.Wpf.DragDrop;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class LayerToolboxView : UserControl, IDropTarget
{
    public LayerToolboxView()
    {
        InitializeComponent();
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (DataContext is LayerToolboxViewModel && dropInfo.Data is LayerItemViewModel)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        dropInfo.Effects = DragDropEffects.None;
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        if (DataContext is LayerToolboxViewModel viewModel && dropInfo.Data is LayerItemViewModel layer)
        {
            viewModel.MoveLayer(layer, dropInfo.InsertIndex);
        }
    }
}
