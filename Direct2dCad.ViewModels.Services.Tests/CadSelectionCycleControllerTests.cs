using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadSelectionCycleControllerTests
{
    [Fact]
    public void Cycle_MovesForwardAndBackwardThroughCandidates()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var second = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var third = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var editor = new CadEditor(document);
        editor.Selection.Add(first.Id);
        var controller = new CadSelectionCycleController();
        controller.Begin(new CadSelectionCycleSeed(
            [],
            [first.Id, second.Id, third.Id],
            CadSelectionMode.Replace));

        Assert.True(controller.Cycle(editor, backwards: false, _ => true));
        Assert.Equal([second.Id], editor.Selection.EntityIds);

        Assert.True(controller.Cycle(editor, backwards: false, _ => true));
        Assert.Equal([third.Id], editor.Selection.EntityIds);

        Assert.True(controller.Cycle(editor, backwards: true, _ => true));
        Assert.Equal([second.Id], editor.Selection.EntityIds);
    }

    [Fact]
    public void Cycle_AddModePreservesBaseSelectionAndSkipsFilteredCandidate()
    {
        var document = CadDocument.Create("Test");
        var selected = document.AddLine(new CadPointD(0, 0), new CadPointD(1, 0));
        var filtered = document.AddLine(new CadPointD(0, 1), new CadPointD(1, 1));
        var allowed = document.AddLine(new CadPointD(0, 2), new CadPointD(1, 2));
        var editor = new CadEditor(document);
        editor.Selection.Add(selected.Id);
        var controller = new CadSelectionCycleController();
        controller.Begin(new CadSelectionCycleSeed(
            [selected.Id],
            [selected.Id, filtered.Id, allowed.Id],
            CadSelectionMode.Add));

        Assert.True(controller.Cycle(
            editor,
            backwards: false,
            entity => entity.Id != filtered.Id));

        Assert.Contains(selected.Id, editor.Selection.EntityIds);
        Assert.Contains(allowed.Id, editor.Selection.EntityIds);
        Assert.DoesNotContain(filtered.Id, editor.Selection.EntityIds);
    }

    [Fact]
    public void ClearOrEmptySeedDisablesCycling()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));
        var controller = new CadSelectionCycleController();

        controller.Begin(null);
        Assert.False(controller.Cycle(editor, backwards: false, _ => true));

        controller.Begin(new CadSelectionCycleSeed([], [], CadSelectionMode.Replace));
        Assert.False(controller.Cycle(editor, backwards: false, _ => true));
    }
}
