using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadSelectionDragController
{
    private CadPointD? _dragStart;

    public bool IsDragging => _dragStart is not null;

    public void Begin(CadPointD screen)
    {
        _dragStart = screen;
    }

    public bool Complete(CadSelectionInteractionService selectionService, CadPointD endScreen)
    {
        if (_dragStart is null)
            return false;

        var startScreen = _dragStart.Value;
        _dragStart = null;
        selectionService.CompleteSelection(startScreen, endScreen);
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
    }
}
