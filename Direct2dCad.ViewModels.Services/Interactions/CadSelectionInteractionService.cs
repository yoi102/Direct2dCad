using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Styling;

namespace Direct2dCad.ViewModels.Interactions;

internal sealed class CadSelectionInteractionService(
    CadEditor editor,
    CadViewport viewport,
    CadPreviewStyleService styleService)
{
    public void CompleteSelection(CadPointD startScreen, CadPointD endScreen)
    {
        if ((endScreen - startScreen).Length < 4)
        {
            editor.Execute(new ClickSelectCommand(
                viewport.ScreenToWorld(endScreen),
                6.0 / viewport.Zoom));
            return;
        }

        var p1 = viewport.ScreenToWorld(startScreen);
        var p2 = viewport.ScreenToWorld(endScreen);
        var area = CadRectD.FromLTRB(p1.X, p1.Y, p2.X, p2.Y);
        editor.Execute(new BoxSelectCommand(
            area,
            requireContained: IsSelectionWindow(startScreen, endScreen)));
    }

    public void AddWindowPreview(
        List<CadTransientItem> items,
        CadPointD? startScreen,
        CadPointD mousePoint)
    {
        if (startScreen is null || (mousePoint - startScreen.Value).Length < 4)
            return;

        items.Add(new CadTransientRectangle(
            ToWorldRect(startScreen.Value, mousePoint),
            IsSelectionWindow(startScreen.Value, mousePoint)
                ? styleService.CreateSelectionWindowStyle()
                : styleService.CreateSelectionCrossingStyle()));
    }

    private CadRectD ToWorldRect(CadPointD startScreen, CadPointD endScreen)
    {
        var p1 = viewport.ScreenToWorld(startScreen);
        var p2 = viewport.ScreenToWorld(endScreen);
        return CadRectD.FromLTRB(p1.X, p1.Y, p2.X, p2.Y);
    }

    private static bool IsSelectionWindow(CadPointD startScreen, CadPointD endScreen)
    {
        return endScreen.X >= startScreen.X;
    }
}
