using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes;
using GongSolutions.Wpf.DragDrop;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class LayersToolboxView : UserControl, IDropTarget
{
    public LayersToolboxView()
    {
        InitializeComponent();
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (DataContext is LayersToolboxViewModel && dropInfo.Data is LayerItemViewModel)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        dropInfo.Effects = DragDropEffects.None;
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        if (DataContext is LayersToolboxViewModel viewModel && dropInfo.Data is LayerItemViewModel layer)
        {
            viewModel.MoveLayer(layer, dropInfo.InsertIndex);
        }
    }
}
