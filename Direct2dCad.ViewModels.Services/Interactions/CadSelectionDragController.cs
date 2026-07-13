using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadSelectionDragController
{
    private CadPointD? _dragStart;
    private CadSelectionMode _selectionMode = CadSelectionMode.Replace;

    public bool IsDragging => _dragStart is not null;

    public void Begin(CadPointD screen, CadSelectionMode selectionMode)
    {
        _dragStart = screen;
        _selectionMode = selectionMode;
    }

    public bool Complete(
        CadSelectionInteractionService selectionService,
        CadPointD endScreen,
        out CadSelectionCycleSeed? cycleSeed)
    {
        if (_dragStart is null)
        {
            cycleSeed = null;
            return false;
        }

        var startScreen = _dragStart.Value;
        _dragStart = null;
        var selectionMode = _selectionMode;
        _selectionMode = CadSelectionMode.Replace;
        cycleSeed = selectionService.CompleteSelection(startScreen, endScreen, selectionMode);
        return true;
    }

    public void AddPreview(
        CadSelectionInteractionService selectionService,
        List<CadTransientItem> items,
        CadPointD mousePoint)
    {
        selectionService.AddWindowPreview(items, _dragStart, mousePoint);
    }

    public void Clear()
    {
        _dragStart = null;
        _selectionMode = CadSelectionMode.Replace;
    }
}
