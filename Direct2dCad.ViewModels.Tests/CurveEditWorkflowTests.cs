using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CurveEditWorkflowTests
{
    private static void Click(CadDocumentViewModel vm,CadPointD point)
    {var screen=vm.CadEditor.Viewport.WorldToScreen(point);vm.PointerMove(screen);vm.PointerDown(screen,CadCanvasPointerButton.Left,false);vm.PointerUp(screen,CadCanvasPointerButton.Left);}
    [Fact]public void OffsetPreviewIsPureAndEscapePreservesEachCommittedEdit()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);var line=vm.CadEditor.AddLine(default,new(100,0));
        vm.SelectEntities([line]);vm.SetToolMode(CadCanvasToolMode.Offset);vm.EditDistance=2;
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(50,3)));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));Assert.Single(vm.CadEditor.Document.Entities);
        Click(vm,new(50,3));Click(vm,new(50,-3));vm.Escape();
        Assert.Equal(3,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));Assert.Equal(CadCanvasToolMode.Select,vm.CadCanvasToolMode);
        vm.Undo();Assert.Equal(2,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));vm.Undo();Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);
    }
    [Fact]public void PickingUsesRawPointEvenWhenGridSnappingIsEnabled()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);vm.CadEditor.Viewport.SetView(10,new(400,300));
        vm.CadEditor.AddLine(new(.45,.45),new(100,.45));vm.IsGridSnapEnabled=true;vm.SetToolMode(CadCanvasToolMode.Offset);vm.EditDistance=2;
        Click(vm,new(50,.45));Click(vm,new(50,1));Assert.Equal(2,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));
    }
    [Theory][InlineData(CadCanvasToolMode.Fillet)][InlineData(CadCanvasToolMode.Chamfer)]
    public void CornerNeedsOneConfirmationAfterTwoObjectPicks(CadCanvasToolMode mode)
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);vm.CadEditor.AddLine(default,new(100,0));vm.CadEditor.AddLine(default,new(0,100));
        vm.SetToolMode(mode);vm.EditDistance=2;vm.EditSecondDistance=3;Click(vm,new(90,0));Click(vm,new(0,90));
        Assert.Equal(2,vm.CadEditor.Document.Entities.Count);Click(vm,new(10,10));Assert.Equal(3,vm.CadEditor.Document.Entities.Count);Assert.Empty(vm.StepInputError);
    }
    [Fact]public void ExplicitBreakPointsRemainExactDespiteGridAndOrtho()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;var line=vm.CadEditor.AddLine(default,new(100,0));vm.IsGridSnapEnabled=true;vm.IsOrthoEnabled=true;
        vm.SelectEntities([line]);var cli=new CadCommandLineService();Assert.True(cli.Execute("BREAK",vm).Success);Assert.True(cli.Execute("1.25,0",vm).Success);Assert.True(cli.Execute("DONE",vm).Success);
        var pieces=vm.CadEditor.Document.Entities.Values.OfType<CadLine>().Where(e=>!e.IsErased).ToArray();Assert.Equal(2,pieces.Length);Assert.Contains(pieces,e=>e.End.X==1.25);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BrokenPiecesRemainIndividuallySelectableAfterWheelZoomAndHistory(bool removeGap)
    {
        using var context = new CadToolboxTestContext();
        var model = context.Document;
        model.SetViewportSize(800, 600);
        var source = model.CadEditor.AddLine(new(-80, 0), new(80, 0));
        model.SelectEntities([source]);
        var cli = new CadCommandLineService();
        Assert.True(cli.Execute("BREAK", model).Success);
        Assert.True(cli.Execute("-20,0", model).Success);
        Assert.True(cli.Execute(removeGap ? "20,0" : "DONE", model).Success);
        model.Escape();
        var pieces = model.CadEditor.Document.Entities.Values.OfType<CadLine>().Where(e => !e.IsErased).ToArray();
        Assert.Equal(2, pieces.Length);

        void VerifyPicking()
        {
            foreach (var delta in new[] { 120, 120, -120, -120 })
            {
                model.MouseWheel(new(400, 300), delta);
                model.SelectEntities([]);
                foreach (var piece in pieces)
                {
                    Click(model, piece.Start + (piece.End - piece.Start) * .5);
                    Assert.Equal(piece.Id, Assert.Single(model.CadEditor.Selection.EntityIds));
                    model.SelectEntities([]);
                }
            }
            if (removeGap)
            {
                Click(model, default);
                Assert.Empty(model.CadEditor.Selection.EntityIds);
            }
        }

        VerifyPicking();
        model.Undo();
        Assert.False(model.CadEditor.Document.GetEntity(source).IsErased);
        Click(model, default);
        Assert.Equal(source, Assert.Single(model.CadEditor.Selection.EntityIds));
        model.Redo();
        Assert.True(model.CadEditor.Document.GetEntity(source).IsErased);
        VerifyPicking();
    }
    [Theory][InlineData(CadCanvasToolMode.RectArray)][InlineData(CadCanvasToolMode.PolarArray)]
    public void ArrayPreviewCancelsOrCommitsAsOneHistoryUnit(CadCanvasToolMode mode)
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);var line=vm.CadEditor.AddLine(new(10,0),new(20,0));vm.SelectEntities([line]);
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();vm.SetToolMode(mode);if(mode==CadCanvasToolMode.PolarArray) Click(vm,default);
        vm.PointerMove(new(400,300));Assert.True(vm.CadEditor.DocumentHistoryEquals(history));vm.Escape();Assert.Single(vm.CadEditor.Document.Entities);
        vm.SelectEntities([line]);vm.SetToolMode(mode);if(mode==CadCanvasToolMode.PolarArray) Click(vm,default);Click(vm,new(40,40));
        Assert.Equal(6,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));vm.Undo();Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);vm.Redo();Assert.Equal(6,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));
    }
    [Fact]public void InvalidOffsetRetainsParametersAndDoesNotAddHistory()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);var circle=vm.CadEditor.AddCircle(default,2);vm.SelectEntities([circle]);vm.SetToolMode(CadCanvasToolMode.Offset);vm.EditDistance=3;
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();Click(vm,default);Assert.NotEmpty(vm.StepInputError);Assert.Equal(3,vm.EditDistance);Assert.True(vm.CadEditor.DocumentHistoryEquals(history));Assert.Single(vm.CadEditor.Document.Entities);
    }
}
