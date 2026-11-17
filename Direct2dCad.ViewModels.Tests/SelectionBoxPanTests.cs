using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

/// <summary>A selection box keeps its first corner on the drawing while the view pans underneath it.</summary>
public sealed class SelectionBoxPanTests
{
    [Fact]
    public void PanningDuringABoxKeepsItsFirstCornerOnTheDrawing()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(CadCanvasToolMode.Select);
        var startWorld = vm.CadEditor.Viewport.ScreenToWorld(new(100, 100));

        vm.PointerDown(new(100, 100), CadCanvasPointerButton.Left, false);
        vm.PointerMove(new(150, 150));
        vm.PointerDown(new(150, 150), CadCanvasPointerButton.Right, false);
        vm.PointerMove(new(250, 150));
        var rightUp = vm.PointerUp(new(250, 150), CadCanvasPointerButton.Right);
        Assert.False(vm.IsPanning);
        Assert.False(rightUp.ReleaseMouseCapture); // the box still waits for the left button
        // The view moved 100 px right, so the pressed point is now at x = 200 on screen.
        Assert.True(vm.CadEditor.Viewport.WorldToScreen(startWorld).NearEquals(new(200, 100)));

        CadPointD World(double x, double y) => vm.CadEditor.Viewport.ScreenToWorld(new(x, y));
        var inside = vm.CadEditor.AddLine(World(230, 140), World(270, 160));
        // Only a box that stayed glued to the screen (x 100..300) would contain this one.
        var leftOfAnchor = vm.CadEditor.AddLine(World(120, 140), World(180, 160));

        vm.PointerMove(new(300, 200));
        vm.PointerUp(new(300, 200), CadCanvasPointerButton.Left);

        Assert.Equal(inside, Assert.Single(vm.CadEditor.Selection.EntityIds));
        Assert.DoesNotContain(leftOfAnchor, vm.CadEditor.Selection.EntityIds);
    }

    [Fact]
    public void ReleasingTheLeftButtonDuringARightButtonPanFinishesTheBoxAndKeepsPanning()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(CadCanvasToolMode.Select);
        CadPointD World(double x, double y) => vm.CadEditor.Viewport.ScreenToWorld(new(x, y));
        var inside = vm.CadEditor.AddLine(World(120, 120), World(180, 180));

        vm.PointerDown(new(100, 100), CadCanvasPointerButton.Left, false);
        vm.PointerMove(new(200, 200));
        vm.PointerDown(new(200, 200), CadCanvasPointerButton.Right, false);
        var leftUp = vm.PointerUp(new(200, 200), CadCanvasPointerButton.Left);

        Assert.True(leftUp.Handled);
        Assert.False(leftUp.ReleaseMouseCapture);
        Assert.True(vm.IsPanning);
        Assert.Equal(inside, Assert.Single(vm.CadEditor.Selection.EntityIds));

        vm.PointerMove(new(260, 200));
        vm.PointerUp(new(260, 200), CadCanvasPointerButton.Right);
        Assert.False(vm.IsPanning);
        Assert.Equal(inside, Assert.Single(vm.CadEditor.Selection.EntityIds));
    }
}
