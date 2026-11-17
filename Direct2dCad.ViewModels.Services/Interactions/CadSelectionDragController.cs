using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

/// <summary>
/// The first corner is anchored to the drawing, not to the screen: panning or zooming while the box is
/// open moves that corner with the drawing, so the box still starts where the user pressed.
/// </summary>
internal sealed class CadSelectionDragController
{
    private CadPointD? _dragStartWorld;
    private CadSelectionMode _selectionMode = CadSelectionMode.Replace;

    public bool IsDragging => _dragStartWorld is not null;

    public void Begin(CadPointD startWorld, CadSelectionMode selectionMode)
    {
        _dragStartWorld = startWorld;
        _selectionMode = selectionMode;
    }

    public bool Complete(
        CadSelectionInteractionService selectionService,
        Func<CadPointD, CadPointD> worldToScreen,
        CadPointD endScreen,
        out CadSelectionCycleSeed? cycleSeed)
    {
        if (_dragStartWorld is null)
        {
            cycleSeed = null;
            return false;
        }

        var startScreen = worldToScreen(_dragStartWorld.Value);
        _dragStartWorld = null;
        var selectionMode = _selectionMode;
        _selectionMode = CadSelectionMode.Replace;
        cycleSeed = selectionService.CompleteSelection(startScreen, endScreen, selectionMode);
        return true;
    }

    public void AddPreview(
        CadSelectionInteractionService selectionService,
        Func<CadPointD, CadPointD> worldToScreen,
        List<CadTransientItem> items,
        CadPointD mousePoint)
    {
        selectionService.AddWindowPreview(
            items,
            _dragStartWorld is { } start ? worldToScreen(start) : null,
            mousePoint);
    }

    public void Clear()
    {
        _dragStartWorld = null;
        _selectionMode = CadSelectionMode.Replace;
    }
}
