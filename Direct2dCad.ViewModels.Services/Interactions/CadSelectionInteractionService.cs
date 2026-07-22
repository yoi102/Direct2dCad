using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal readonly struct CadSelectionInteractionService(
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
                selectionFilter: selectionFilter,
                ownerBlockId: editor.ActiveOwnerBlockId);
            editor.Execute(command);
            return command.SelectedEntityId is null
                ? null
                : new CadSelectionCycleSeed(
                    baseSelection,
                    command.HitEntityIds,
                    selectionMode);
        }

        var corners = ToWorldPolygon(startScreen, endScreen);
        var requireContained = IsSelectionWindow(startScreen, endScreen);
        if (IsAxisAligned(corners))
        {
            editor.Execute(new BoxSelectCommand(
                corners.Aggregate(CadRectD.Empty, static (bounds, point) => bounds.ExpandToInclude(point)),
                selectionMode,
                requireContained,
                selectionFilter,
                zoom,
                editor.ActiveOwnerBlockId));
        }
        else
        {
            editor.Execute(new PolygonSelectCommand(
                corners,
                selectionMode,
                requireContained,
                selectionFilter,
                zoom,
                editor.ActiveOwnerBlockId));
        }
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

    private CadPointD[] ToWorldPolygon(CadPointD startScreen, CadPointD endScreen)
    {
        var left = Math.Min(startScreen.X, endScreen.X);
        var right = Math.Max(startScreen.X, endScreen.X);
        var top = Math.Min(startScreen.Y, endScreen.Y);
        var bottom = Math.Max(startScreen.Y, endScreen.Y);
        return
        [
            screenToWorld(new CadPointD(left, top)),
            screenToWorld(new CadPointD(right, top)),
            screenToWorld(new CadPointD(right, bottom)),
            screenToWorld(new CadPointD(left, bottom))
        ];
    }

    private static bool IsAxisAligned(IReadOnlyList<CadPointD> corners)
    {
        const double tolerance = 1e-9;
        return Math.Abs(corners[0].Y - corners[1].Y) <= tolerance &&
               Math.Abs(corners[1].X - corners[2].X) <= tolerance &&
               Math.Abs(corners[2].Y - corners[3].Y) <= tolerance &&
               Math.Abs(corners[3].X - corners[0].X) <= tolerance;
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
