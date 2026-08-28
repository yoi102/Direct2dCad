using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadSelectionInteractionServiceTests
{
    [Fact]
    public void AddWindowPreview_UsesWindowOrCrossingStyleAndSkipsSmallDrags()
    {
        var document = CadDocument.Create("Selection preview");
        var settings = CadUserSettings.CreateDefault();
        var editor = new CadEditor(document);
        var service = CreateService(document, editor, settings, static point => point);
        var items = new List<CadTransientItem>();

        service.AddWindowPreview(items, null, new CadPointD(10, 10));
        service.AddWindowPreview(items, new CadPointD(0, 0), new CadPointD(2, 2));
        Assert.Empty(items);

        service.AddWindowPreview(items, new CadPointD(0, 0), new CadPointD(10, 8));
        var window = Assert.IsType<CadTransientPolyline>(Assert.Single(items));
        Assert.True(window.Closed);
        Assert.Equal(settings.Interaction.SelectionWindowStrokeColor, window.Style.StrokeColor);
        Assert.Equal(
            [new CadPointD(0, 0), new CadPointD(10, 0), new CadPointD(10, 8), new CadPointD(0, 8)],
            window.Points);

        items.Clear();
        service.AddWindowPreview(items, new CadPointD(10, 0), new CadPointD(0, 8));
        var crossing = Assert.IsType<CadTransientPolyline>(Assert.Single(items));
        Assert.Equal(settings.Interaction.SelectionCrossingStrokeColor, crossing.Style.StrokeColor);
    }

    [Fact]
    public void CompleteSelection_UsesWindowContainmentAndCrossingIntersection()
    {
        var document = CadDocument.Create("Selection");
        var contained = document.AddLine(new CadPointD(2, 5), new CadPointD(8, 5));
        var crossingOnly = document.AddLine(new CadPointD(5, 7), new CadPointD(15, 7));
        var editor = new CadEditor(document);
        var service = CreateService(document, editor, CadUserSettings.CreateDefault(), static point => point);

        service.CompleteSelection(new CadPointD(0, 0), new CadPointD(10, 10), CadSelectionMode.Replace);
        Assert.Equal([contained.Id], editor.Selection.EntityIds);

        service.CompleteSelection(new CadPointD(10, 0), new CadPointD(0, 10), CadSelectionMode.Replace);
        Assert.Contains(contained.Id, editor.Selection.EntityIds);
        Assert.Contains(crossingOnly.Id, editor.Selection.EntityIds);
    }

    [Fact]
    public void CompleteSelection_UsesPolygonSelectionForNonAxisAlignedWorldTransform()
    {
        var document = CadDocument.Create("Rotated selection");
        var selected = document.AddLine(new CadPointD(7, 5), new CadPointD(8, 5));
        var editor = new CadEditor(document);
        var service = CreateService(
            document,
            editor,
            CadUserSettings.CreateDefault(),
            static point => new CadPointD(point.X + point.Y / 2, point.Y));

        service.CompleteSelection(new CadPointD(0, 0), new CadPointD(10, 10), CadSelectionMode.Replace);

        Assert.Equal([selected.Id], editor.Selection.EntityIds);
    }

    [Fact]
    public void CompleteSelection_ClickReturnsAllCycleCandidatesAndHonorsFilter()
    {
        var document = CadDocument.Create("Click selection");
        var filtered = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var allowed = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var editor = new CadEditor(document);
        var service = CreateService(
            document,
            editor,
            CadUserSettings.CreateDefault(),
            static point => point,
            entity => entity.Id == allowed.Id);

        var seed = service.CompleteSelection(CadPointD.Origin, new CadPointD(1, 0), CadSelectionMode.Replace);

        Assert.NotNull(seed);
        Assert.Equal([allowed.Id], seed.Candidates);
        Assert.Equal([allowed.Id], editor.Selection.EntityIds);
        Assert.DoesNotContain(filtered.Id, editor.Selection.EntityIds);
    }

    private static CadSelectionInteractionService CreateService(
        CadDocument document,
        CadEditor editor,
        CadUserSettings settings,
        Func<CadPointD, CadPointD> screenToWorld,
        Func<CadEntity, bool>? selectionFilter = null)
    {
        return new CadSelectionInteractionService(
            editor,
            screenToWorld,
            new CadHitTestOptions(viewportZoom: 1),
            new CadPreviewStyleService(document, settings),
            selectionFilter ?? (static _ => true));
    }
}
