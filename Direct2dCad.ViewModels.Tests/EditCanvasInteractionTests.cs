using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.CommandLine;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EditCanvasInteractionTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.Offset)] [InlineData(CadCanvasToolMode.Trim)] [InlineData(CadCanvasToolMode.Extend)]
    [InlineData(CadCanvasToolMode.Break)] [InlineData(CadCanvasToolMode.Fillet)] [InlineData(CadCanvasToolMode.Chamfer)]
    [InlineData(CadCanvasToolMode.Join)] [InlineData(CadCanvasToolMode.RectArray)] [InlineData(CadCanvasToolMode.PolarArray)]
    public void EditToolsKeepPreselectionAndOnlyShowApplicableCursorParameters(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var line=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([line]); vm.SetToolMode(mode);
        Assert.Equal(line,Assert.Single(vm.CadEditor.Selection.EntityIds));
        string[] keys = mode switch
        {
            CadCanvasToolMode.Offset => ["EditDistance"], CadCanvasToolMode.Fillet => ["EditRadius"],
            CadCanvasToolMode.Chamfer => ["EditDistance", "EditSecondDistance"],
            CadCanvasToolMode.RectArray => ["EditRows", "EditColumns", "EditSpacingX", "EditSpacingY"],
            CadCanvasToolMode.PolarArray => ["EditCount", "EditSweep"], _ => []
        };
        Assert.Equal(keys, vm.DynamicInputFields.Select(f => f.Key));
        Assert.Equal(keys.Length > 0, vm.HasDynamicInput);
        Assert.NotEmpty(vm.EditInstruction); Assert.NotEmpty(vm.EditActionText);
        vm.Escape(); Assert.False(vm.IsCurveEditTool);
    }

    [Fact]
    public void OffsetPreviewRemainsWhenPointerLeavesCanvasAndChangingDistanceChangesActualGeometry()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var line=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([line]); vm.SetToolMode(CadCanvasToolMode.Offset);
        Move(vm,new(50,5)); vm.PointerLeave(); var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.EditDistance=25;
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(),p=>p.Start.Y==25 && p.End.Y==25);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Assert.Single(vm.CadEditor.Document.Entities);
        Click(vm,new(50,5)); Assert.Equal(2,vm.CadEditor.Document.Entities.Count); vm.Undo();
        Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);
    }

    [Theory] [InlineData(CadCanvasToolMode.Trim)] [InlineData(CadCanvasToolMode.Extend)]
    public void BoundarySelectionCanAdvanceWithEnterAndPreviewTheChosenSegment(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        vm.CadEditor.AddLine(new(50,-30),new(50,30));
        vm.CadEditor.AddLine(default,new(mode==CadCanvasToolMode.Trim ? 100 : 25,0));
        vm.SetToolMode(mode); Click(vm,new(50,20)); Assert.True(vm.CanAdvanceEdit);
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); vm.FinishStepCommand.Execute(null);
        var point=new CadPointD(mode==CadCanvasToolMode.Trim ? 75 : 20,0); Move(vm,point);
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(),p=>p.Start.Y==0 && p.End.Y==0 &&
            p.Style.StrokeColor==(mode==CadCanvasToolMode.Trim ? CadColor.FromArgb(255,255,92,92) : CadColor.FromArgb(255,92,230,145)));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Click(vm,point); Assert.Empty(vm.StepInputError);
        vm.Undo(); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory] [InlineData(CadCanvasToolMode.Fillet)] [InlineData(CadCanvasToolMode.Chamfer)]
    public void CornerPreviewsOnHoverBeforeSecondPickAndCanBeCancelledWithoutChangingHistory(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        vm.CadEditor.AddLine(default,new(100,0)); vm.CadEditor.AddLine(default,new(0,100)); vm.SetToolMode(mode);
        vm.EditDistance=8; vm.EditSecondDistance=12; Click(vm,new(90,0)); Move(vm,new(0,90));
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); var items=vm.CreateTransientItems();
        if(mode==CadCanvasToolMode.Fillet) Assert.Contains(items.OfType<CadTransientArc>(),a=>Math.Abs(a.Radius-8)<1e-8);
        else Assert.Contains(items.OfType<CadTransientLine>(),p=>p.Start.NearEquals(new(8,0)) && p.End.NearEquals(new(0,12)));
        Assert.Single(vm.CadEditor.Selection.EntityIds); Assert.False(vm.CanAdvanceEdit);
        Click(vm,new(0,90)); Assert.True(vm.CanAdvanceEdit); vm.PointerLeave();
        Assert.NotEmpty(vm.CreateTransientItems()); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Escape(); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory] [InlineData(CadCanvasToolMode.Fillet)] [InlineData(CadCanvasToolMode.Chamfer)]
    public void CornerPreviewKeepsTheHoveredSecondCurveWhenMouseMovesAwayOrSizesChange(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        vm.CadEditor.AddLine(default, new(100, 0)); vm.CadEditor.AddLine(default, new(0, 100));
        vm.SetToolMode(mode); vm.EditDistance = 8; vm.EditSecondDistance = 12;
        Click(vm, new(90, 0)); Move(vm, new(0, 90));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Move(vm, new(50, 50)); vm.DynamicInputFields[0].Text = "10";
        var items = vm.CreateTransientItems();
        if(mode == CadCanvasToolMode.Fillet)
            Assert.Contains(items.OfType<CadTransientArc>(), arc => Math.Abs(arc.Radius - 10) < 1e-8);
        else Assert.Contains(items.OfType<CadTransientLine>(), line =>
            line.Start.NearEquals(new(10, 0)) && line.End.NearEquals(new(0, 12)));
        Assert.Single(vm.CadEditor.Selection.EntityIds);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.ReselectEditObjectsCommand.Execute(null);
        Assert.Empty(vm.CreateTransientItems().OfType<CadTransientArc>());
        Assert.DoesNotContain(vm.CreateTransientItems().OfType<CadTransientLine>(),
            line => line.Style.StrokeColor == CadColor.FromArgb(255, 92, 230, 145));
    }

    [Fact]
    public void BreakKeepsItsFirstMarkerAndPreviewsRetainedPiecesBeforeConfirmation()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        vm.IsObjectSnapEnabled=false;
        var line=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([line]); vm.SetToolMode(CadCanvasToolMode.Break);
        Click(vm,new(20,0)); Move(vm,new(60,0)); vm.PointerLeave();
        var items=vm.CreateTransientItems();
        Assert.Contains(items.OfType<CadTransientCircle>(),p=>p.Center.NearEquals(new(20,0)));
        Assert.Contains(items.OfType<CadTransientLine>(),p=>p.Start.NearEquals(default) && p.End.NearEquals(new(20,0)));
        Assert.Contains(items.OfType<CadTransientLine>(),p=>p.Start.NearEquals(new(60,0)) && p.End.NearEquals(new(100,0)));
        Assert.True(vm.CanAdvanceEdit); Assert.Single(vm.CadEditor.Document.Entities);
        Click(vm,new(60,0)); Assert.Equal(2,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));
        vm.Undo(); Assert.False(vm.CadEditor.Document.GetEntity(line).IsErased);
    }

    [Fact]
    public void JoinPreviewsTheResultAndAllowsPickedCurvesToBeDeselected()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var first=vm.CadEditor.AddLine(default,new(50,0)); var second=vm.CadEditor.AddLine(new(50,0),new(100,0));
        vm.SelectEntities([first,second]); vm.SetToolMode(CadCanvasToolMode.Join);
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); vm.PointerLeave();
        Assert.Equal(2,vm.CreateTransientItems().OfType<CadTransientLine>().Count());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Click(vm,new(75,0)); Assert.False(vm.CanAdvanceEdit);
        Move(vm,new(75,0)); Assert.Equal(2,vm.CreateTransientItems().OfType<CadTransientLine>().Count());
        Click(vm,new(75,0)); Assert.True(vm.CanAdvanceEdit); vm.FinishStepCommand.Execute(null);
        Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased); vm.Undo();
        Assert.Equal(2,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));
    }

    [Theory] [InlineData(CadCanvasToolMode.RectArray)] [InlineData(CadCanvasToolMode.PolarArray)]
    public void ArrayParametersChangePreviewAndPolarCenterCanBePreviewedBeforePicking(CadCanvasToolMode mode)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(new(10,0),new(20,0)); vm.SelectEntities([source]); vm.SetToolMode(mode);
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); Move(vm,default);
        Assert.Equal(5,vm.CreateTransientItems().OfType<CadTransientGroup>().Count());
        if(mode==CadCanvasToolMode.PolarArray) { Assert.True(vm.CanAdvanceEdit); vm.ArrayCount=8; }
        else { vm.ArrayRows=2; vm.ArrayColumns=4; vm.ArraySpacingX=30; }
        vm.PointerLeave(); Assert.Equal(7,vm.CreateTransientItems().OfType<CadTransientGroup>().Count());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); vm.FinishStepCommand.Execute(null);
        Assert.Equal(8,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased)); vm.Undo();
        Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);
    }

    [Fact]
    public void InvalidEditorTextAndInvalidArrayValuesCannotCommitOrLeaveTheStep()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.RectArray);
        var history=vm.CadEditor.CreateDocumentHistorySnapshot(); vm.SetEditParameterInputValid(false);
        Assert.False(vm.CanAdvanceEdit); vm.FinishStepCommand.Execute(null); Click(vm,new(30,30));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Assert.Equal(CadCanvasToolMode.RectArray,vm.CadCanvasToolMode);
        vm.SetEditParameterInputValid(true); vm.ArraySpacingX=0; Assert.False(vm.CanAdvanceEdit);
        Assert.NotEmpty(vm.EditFeedback); vm.FinishStepCommand.Execute(null); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.ArraySpacingX=20; Assert.True(vm.CanAdvanceEdit);
    }

    [Fact]
    public void ReselectKeepsParametersAndCannotDiscardAnInvalidEditorValue()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset);
        vm.EditDistance=30; vm.SetEditParameterInputValid(false); vm.ReselectEditObjectsCommand.Execute(null);
        Assert.Empty(vm.CadEditor.Selection.EntityIds); Assert.Equal(30,vm.EditDistance); Assert.False(vm.CanAdvanceEdit);
        vm.SetEditParameterInputValid(true); Assert.Empty(vm.EditFeedback); Click(vm,new(50,0));
        Assert.Equal(source,Assert.Single(vm.CadEditor.Selection.EntityIds)); Assert.Equal(30,vm.EditDistance);
    }

    [Fact]
    public void LargeArraysExplainThePreviewLimitInsteadOfSilentlyShowingAFewCopies()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(new(10,0),new(20,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.PolarArray);
        Move(vm,default); vm.ArrayCount=300;
        Assert.Equal(255,vm.CreateTransientItems().OfType<CadTransientGroup>().Count()); Assert.NotEmpty(vm.EditFeedback);
    }

    [Fact]
    public void PolarCursorParametersPreviewImmediatelyAndFieldEnterDoesNotCommit()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(new(80,0),new(120,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.PolarArray);
        Move(vm,default); var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.Equal(new[]{"N:","A:"},vm.DynamicInputFields.Select(f=>f.Prefix));
        vm.DynamicInputFields[0].Text="5"; vm.DynamicInputFields[1].Text="40";
        Assert.Equal(5,vm.ArrayCount); Assert.Equal(40,vm.ArraySweepDegrees);
        Assert.Equal(4,vm.CreateTransientItems().OfType<CadTransientGroup>().Count());
        Assert.True(vm.SubmitDynamicInput()); Assert.True(vm.CanAdvanceEdit); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm,default);
        Assert.Equal(5,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased)); vm.Undo();
        Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);
    }

    [Theory]
    [InlineData("2.5")] [InlineData("bad")] [InlineData("Infinity")] [InlineData("2147483648")]
    public void InvalidArrayCursorTextCannotCommitAndCanBeCorrectedWithoutLosingOtherFields(string text)
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.RectArray);
        vm.DynamicInputFields[2].Text="60"; vm.DynamicInputFields[0].Text=text;
        var history=vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(vm.CanAdvanceEdit); Assert.False(vm.SubmitDynamicInput()); Click(vm,new(30,30));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history)); Assert.NotEmpty(vm.DynamicInputError);
        vm.DynamicInputFields[0].Text="2"; Assert.True(vm.CanAdvanceEdit); Assert.Empty(vm.DynamicInputError);
        Assert.Equal("60",vm.DynamicInputFields[2].Text); Assert.Equal(60,vm.ArraySpacingX);
    }

    [Fact]
    public void OffsetCursorDistanceCanCommitByMouseAndReselectKeepsTheTypedValue()
    {
        using var context=new CadToolboxTestContext(); var vm=Prepare(context);
        var source=vm.CadEditor.AddLine(default,new(100,0)); vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset);
        vm.DynamicInputFields[0].Text="30"; vm.ReselectEditObjectsCommand.Execute(null);
        Assert.Equal("30",vm.DynamicInputFields[0].Text); Click(vm,new(50,0)); Move(vm,new(50,60));
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(),line=>line.Start.Y==30);
        Click(vm,new(50,60)); Assert.Equal(2,vm.CadEditor.Document.Entities.Values.Count(e=>!e.IsErased));
    }

    [Theory]
    [InlineData(50, 20)] [InlineData(150, -25)]
    public void OffsetMouseDistanceUsesTheCurveNormalAndCommitsThePreview(double x, double y)
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        var source = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Move(vm, new(x, y));
        Assert.Equal(Math.Abs(y), vm.EditDistance, 8);
        Assert.False(vm.DynamicInputFields[0].IsLocked);
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(), line =>
            Math.Abs(line.Start.Y - y) < 1e-8 && Math.Abs(line.End.Y - y) < 1e-8);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm, new(x, y));
        Assert.Contains(vm.CadEditor.Document.Entities.Values.OfType<CadLine>(), line =>
            line.Id != source && Math.Abs(line.Start.Y - y) < 1e-8);
        vm.Undo(); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory] [InlineData(15, 5)] [InlineData(6, 4)]
    public void CircleOffsetMouseDistanceIsRadial(double cursorRadius, double distance)
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        var source = vm.CadEditor.AddCircle(default, 10);
        vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset);
        Move(vm, new(cursorRadius, 0)); Assert.Equal(distance, vm.EditDistance, 8);
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientArc>(), arc => Math.Abs(arc.Radius - cursorRadius) < 1e-8);
        Click(vm, new(cursorRadius, 0));
        Assert.Contains(vm.CadEditor.Document.Entities.Values.OfType<CadCircle>(), circle =>
            circle.Id != source && Math.Abs(circle.Radius - cursorRadius) < 1e-8);
    }

    [Fact]
    public void OffsetTypedDistanceSurvivesPointerMovesAndUnlockResumesMouseTracking()
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        var source = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset);
        Move(vm, new(50, 20)); vm.DynamicInputFields[0].Text = "30";
        Move(vm, new(50, -70)); Assert.Equal(30, vm.EditDistance);
        Assert.Contains(vm.CreateTransientItems().OfType<CadTransientLine>(), line => line.Start.Y == -30);
        vm.DynamicInputFields[0].Text = "bad"; Move(vm, new(50, 80));
        Assert.Equal("bad", vm.DynamicInputFields[0].Text);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot(); Click(vm, new(50, 80));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.ClearDynamicInputLocks(); Assert.Equal(80, vm.EditDistance);
        Assert.False(vm.DynamicInputFields[0].IsLocked); Assert.Empty(vm.DynamicInputError);
        Move(vm, new(50, -40)); Click(vm, new(50, -40));
        Assert.Contains(vm.CadEditor.Document.Entities.Values.OfType<CadLine>(), line => line.Id != source && line.Start.Y == -40);
    }

    [Fact]
    public void OffsetCanFinishWithEnterWhenPointerIsOnTheSource()
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        var source = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([source]); vm.SetToolMode(CadCanvasToolMode.Offset); Move(vm, new(50, 0));
        Assert.Equal(0, vm.EditDistance); Assert.Empty(vm.EditFeedback);
        vm.CompleteCurrentDrawing(); Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.Single(vm.CadEditor.Document.Entities);
    }

    [Fact]
    public void TerminalReceivesToolAndStepPromptsWithoutPointerOrPreviewSpam()
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        using var workspace = new ToolExecutionWorkspace();
        using var terminal = new CommandLineToolboxViewModel(context.Platform, context.Platform, new CadCommandLineService(),
            new CadToolCommandLineService(workspace), context.GetService<IAsyncSubscriber<CadCommandActivityMessage>>(),
            context.GetService<IAsyncSubscriber<CadInteractionActivityMessage>>());
        terminal.Attach(vm); terminal.FlushPendingEntries();
        vm.CadEditor.AddLine(default, new(100, 0)); vm.SetToolMode(CadCanvasToolMode.Break);
        terminal.FlushPendingEntries();
        Assert.Contains(terminal.Entries, entry => entry.Text.EndsWith(vm.CurrentStepPrompt));
        Click(vm, new(50, 0)); terminal.FlushPendingEntries();
        Click(vm, new(20, 0)); terminal.FlushPendingEntries();
        var secondPrompt = vm.CurrentStepPrompt;
        Assert.Single(terminal.Entries, entry => entry.Text.EndsWith(secondPrompt));
        for (var x = 25; x < 40; x++) { Move(vm, new(x, 0)); vm.CreateTransientItems(); }
        terminal.FlushPendingEntries();
        Assert.Single(terminal.Entries, entry => entry.Text.EndsWith(secondPrompt));
        vm.Escape(); vm.SetToolMode(CadCanvasToolMode.Line); terminal.FlushPendingEntries();
        Assert.Contains(terminal.Entries, entry => entry.Text.EndsWith(vm.CurrentStepPrompt));
        Click(vm, new(150, 50)); terminal.FlushPendingEntries();
        Assert.Contains(terminal.Entries, entry => entry.Text.EndsWith(vm.CurrentStepPrompt));
    }

    [Theory] [InlineData(CadCanvasToolMode.RectArray)] [InlineData(CadCanvasToolMode.PolarArray)]
    public void ArrayFirstPickPreviewsAndSecondClickCommitsWithoutAdvancingSelection(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext(); var vm = Prepare(context);
        var source = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SetToolMode(mode);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Click(vm, new(50, 0));
        Assert.True(vm.CanAdvanceEdit);
        Assert.Equal(5, vm.CreateTransientItems().OfType<CadTransientGroup>().Count());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        if(mode == CadCanvasToolMode.RectArray)
        { vm.DynamicInputFields[0].Text = "3"; vm.DynamicInputFields[1].Text = "2"; }
        else vm.DynamicInputFields[0].Text = "6";
        Click(vm, new(120, 60));
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.Equal(6, vm.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        vm.Undo(); Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.SelectEntities([]); vm.SetToolMode(mode); Click(vm, new(50, 0)); vm.Escape();
        Assert.Single(vm.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
    }

    private static CadDocumentViewModel Prepare(CadToolboxTestContext context)
    { var vm=context.Document; vm.SetViewportSize(800,600); return vm; }
    private static void Move(CadDocumentViewModel vm,CadPointD point) => vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(point));
    private static void Click(CadDocumentViewModel vm,CadPointD point)
    {
        var screen=vm.CadEditor.Viewport.WorldToScreen(point); vm.PointerMove(screen);
        vm.PointerDown(screen,CadCanvasPointerButton.Left,false); vm.PointerUp(screen,CadCanvasPointerButton.Left);
    }
}
