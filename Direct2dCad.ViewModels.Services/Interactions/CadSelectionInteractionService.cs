using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadSelectionInteractionService(
    CadEditor editor,
    Func<CadPointD, CadPointD> screenToWorld,
    double zoom,
    CadPreviewStyleService styleService,
    Func<CadEntity, bool> selectionFilter)
{
    public CadSelectionCycleSeed? CompleteSelection(
        CadPointD startScreen,
        CadPointD endScreen,
        CadSelectionMode selectionMode)
    {
        if ((endScreen - startScreen).Length < 4)
        {
            var baseSelection = editor.Selection.EntityIds.ToArray();
            var command = new ClickSelectCommand(
                screenToWorld(endScreen),
                6.0 / Math.Max(zoom, double.Epsilon),
                selectionMode,
                selectionFilter: selectionFilter);
            editor.Execute(command);
            return command.SelectedEntityId is null
                ? null
                : new CadSelectionCycleSeed(
                    baseSelection,
                    command.HitEntityIds,
                    selectionMode);
        }

        var area = ToWorldRect(startScreen, endScreen);
        editor.Execute(new BoxSelectCommand(
            area,
            selectionMode,
            requireContained: IsSelectionWindow(startScreen, endScreen),
            selectionFilter: selectionFilter,
            viewportZoom: zoom));
        return null;
    }

    public void AddWindowPreview(
        List<CadTransientItem> items,
        CadPointD? startScreen,
        CadPointD mousePoint)
    {
        if (startScreen is null || (mousePoint - startScreen.Value).Length < 4)
            return;

        var left = Math.Min(startScreen.Value.X, mousePoint.X);
        var right = Math.Max(startScreen.Value.X, mousePoint.X);
        var top = Math.Min(startScreen.Value.Y, mousePoint.Y);
        var bottom = Math.Max(startScreen.Value.Y, mousePoint.Y);
        items.Add(new CadTransientPolyline(
            [
                screenToWorld(new CadPointD(left, top)),
                screenToWorld(new CadPointD(right, top)),
                screenToWorld(new CadPointD(right, bottom)),
                screenToWorld(new CadPointD(left, bottom))
            ],
            true,
            IsSelectionWindow(startScreen.Value, mousePoint)
                ? styleService.CreateSelectionWindowStyle()
                : styleService.CreateSelectionCrossingStyle()));
    }

    private CadRectD ToWorldRect(CadPointD startScreen, CadPointD endScreen)
    {
        var left = Math.Min(startScreen.X, endScreen.X);
        var right = Math.Max(startScreen.X, endScreen.X);
        var top = Math.Min(startScreen.Y, endScreen.Y);
        var bottom = Math.Max(startScreen.Y, endScreen.Y);
        return CadRectD.Empty
            .ExpandToInclude(screenToWorld(new CadPointD(left, top)))
            .ExpandToInclude(screenToWorld(new CadPointD(right, top)))
            .ExpandToInclude(screenToWorld(new CadPointD(right, bottom)))
            .ExpandToInclude(screenToWorld(new CadPointD(left, bottom)));
    }

    private static bool IsSelectionWindow(CadPointD startScreen, CadPointD endScreen)
    {
        return endScreen.X >= startScreen.X;
    }
}

internal sealed record CadSelectionCycleSeed(
    IReadOnlyList<EntityId> BaseSelection,
    IReadOnlyList<EntityId> Candidates,
    CadSelectionMode SelectionMode);
