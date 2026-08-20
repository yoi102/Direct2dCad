using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Direct2dCad.ViewModels.Settings.UserSettings;
using GongSolutions.Wpf.DragDrop;

namespace Direct2dCad.wpf.Views.Settings.UserSettings;

public partial class InteractionUserSettingsView : UserControl, IDropTarget, IDragSource
{
    private const string DeleteTargetTag = "CadRadialMenuDeleteTarget";

    private sealed record RadialMenuSlotDragData(
        RadialMenuSlotViewModel SourceSlot,
        CadRadialMenuActionOption Action);

    public InteractionUserSettingsView()
    {
        InitializeComponent();
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (IsDeleteTarget(dropInfo))
        {
            if (GetSourceSlot(dropInfo) is not null)
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                return;
            }

            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        if (GetAction(dropInfo) is not null &&
            GetTargetSlot(dropInfo) is not null)
        {
            dropInfo.Effects = DragDropEffects.Copy;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            return;
        }

        dropInfo.Effects = DragDropEffects.None;
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        if (IsDeleteTarget(dropInfo) &&
            GetSourceSlot(dropInfo) is { } sourceSlot)
        {
            sourceSlot.Clear();
            return;
        }

        if (GetAction(dropInfo) is { } action &&
            GetTargetSlot(dropInfo) is { } slot)
            slot.SelectedAction = action;
    }

    private static RadialMenuSlotViewModel? GetTargetSlot(IDropInfo dropInfo) =>
        dropInfo.TargetItem as RadialMenuSlotViewModel ??
        (dropInfo.VisualTargetItem as FrameworkElement)?.DataContext as RadialMenuSlotViewModel;

    private bool IsDeleteTarget(IDropInfo dropInfo)
    {
        for (DependencyObject? current = dropInfo.VisualTarget; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement { Tag: DeleteTargetTag })
                return true;
        }

        if (dropInfo.VisualTarget is not UIElement visualTarget)
            return false;

        var position = visualTarget.TranslatePoint(dropInfo.DropPosition, RadialMenuCanvas);
        const double center = 174;
        const double radius = 46;
        var offsetX = position.X - center;
        var offsetY = position.Y - center;
        return (offsetX * offsetX) + (offsetY * offsetY) <= radius * radius;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual or Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private static CadRadialMenuActionOption? GetAction(IDropInfo dropInfo) =>
        dropInfo.Data switch
        {
            CadRadialMenuActionOption action => action,
            RadialMenuSlotDragData slotDragData => slotDragData.Action,
            _ => null
        };

    private static RadialMenuSlotViewModel? GetSourceSlot(IDropInfo dropInfo) =>
        (dropInfo.Data as RadialMenuSlotDragData)?.SourceSlot ??
        dropInfo.DragInfo?.SourceItem as RadialMenuSlotViewModel;

    bool IDragSource.CanStartDrag(IDragInfo dragInfo) =>
        dragInfo.SourceItem is RadialMenuSlotViewModel;

    void IDragSource.StartDrag(IDragInfo dragInfo)
    {
        if (dragInfo.SourceItem is not RadialMenuSlotViewModel slot)
            return;

        dragInfo.Data = new RadialMenuSlotDragData(slot, slot.SelectedAction);
        // Slots may be copied to another sector or moved into the center delete target.
        dragInfo.Effects = DragDropEffects.Copy | DragDropEffects.Move;
    }

    void IDragSource.Dropped(IDropInfo dropInfo)
    {
    }

    void IDragSource.DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
    {
    }

    void IDragSource.DragCancelled()
    {
    }

    bool IDragSource.TryCatchOccurredException(Exception exception) => false;
}
