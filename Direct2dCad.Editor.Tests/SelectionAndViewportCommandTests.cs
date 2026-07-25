using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.Tests;

public sealed class SelectionAndViewportCommandTests
{
    [Fact]
    public void BoxSelect_CrossingSelectsTouchedEntityButWindowRequiresContainment()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var editor = new CadEditor(document);
        var area = CadRectD.FromLTRB(4, -1, 6, 1);

        editor.Execute(new BoxSelectCommand(area, requireContained: false));
        Assert.Contains(line.Id, editor.Selection.EntityIds);

        editor.Execute(new BoxSelectCommand(area, requireContained: true));
        Assert.DoesNotContain(line.Id, editor.Selection.EntityIds);
    }

    [Fact]
    public void BoxSelect_ToggleAndUndoRestorePreviousSelection()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddCircle(new CadPointD(0, 0), 2);
        var second = document.AddCircle(new CadPointD(10, 0), 2);
        var editor = new CadEditor(document);
        editor.Selection.Add(first.Id);

        editor.Execute(new BoxSelectCommand(
            CadRectD.FromLTRB(-3, -3, 13, 3),
            CadSelectionMode.Toggle));

        Assert.DoesNotContain(first.Id, editor.Selection.EntityIds);
        Assert.Contains(second.Id, editor.Selection.EntityIds);

        editor.UndoEditor();

        Assert.Equal([first.Id], editor.Selection.EntityIds);
    }

    [Fact]
    public void PolygonSelect_RespectsSelectionFilter()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));
        var circle = document.AddCircle(new CadPointD(5, 2), 1);
        var editor = new CadEditor(document);
        var polygon = new[]
        {
            new CadPointD(-1, -1),
            new CadPointD(11, -1),
            new CadPointD(11, 4),
            new CadPointD(-1, 4)
        };

        editor.Execute(new PolygonSelectCommand(
            polygon,
            selectionFilter: entity => entity.Id == circle.Id));

        Assert.Equal([circle.Id], editor.Selection.EntityIds);
        Assert.DoesNotContain(line.Id, editor.Selection.EntityIds);
    }

    [Fact]
    public void ClickSelect_ExposesAllOverlappingCandidatesInDisplayOrder()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        first.SetZIndex(1);
        var second = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        second.SetZIndex(3);
        var third = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        third.SetZIndex(2);
        var editor = new CadEditor(document);
        var command = new ClickSelectCommand(new CadPointD(5, 0), 0.1);

        editor.Execute(command);

        Assert.Equal([second.Id, third.Id, first.Id], command.HitEntityIds);
        Assert.Equal(second.Id, command.SelectedEntityId);
    }

    [Fact]
    public void FitViewport_WithoutEntitiesCentersConfiguredOrigin()
    {
        var document = CadDocument.Create("Test");
        document.ViewSettings.Origin.Position = new CadPointD(100, 50);
        var editor = new CadEditor(document);
        editor.Viewport.SetSize(800, 600);
        var command = new FitViewportCommand();

        editor.Execute(command);

        Assert.Equal(1, editor.Viewport.Zoom);
        Assert.Equal(new CadPointD(300, 350), editor.Viewport.Offset);

        editor.UndoEditor();
        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
    }

    [Fact]
    public void FitViewport_IgnoresHiddenAndFrozenLayerEntities()
    {
        var document = CadDocument.Create("Test");
        var visible = document.AddLine(CadPointD.Origin, new CadPointD(100, 50));
        var hidden = document.AddCircle(new CadPointD(10_000, 10_000), 100);
        hidden.SetVisible(false);
        var frozenLayerId = document.CreateLayer("Frozen", CadColor.Green, CadLineWeight.Default);
        document.GetLayer(frozenLayerId).SetFrozen(true);
        document.AddCircle(new CadPointD(-10_000, -10_000), 100, frozenLayerId);
        var editor = new CadEditor(document);
        editor.Viewport.SetSize(1000, 600);

        editor.Execute(new FitViewportCommand(padding: 50));

        Assert.Equal(9, editor.Viewport.Zoom, 6);
        Assert.Equal(new CadPointD(50, 525), editor.Viewport.Offset);
        Assert.False(visible.IsErased);
    }
}
