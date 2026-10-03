using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class InvalidDrawingInputTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.Line, "0,0", "0,0", "10,10")]
    [InlineData(CadCanvasToolMode.Rectangle, "0,0", "0,10", "10,10")]
    [InlineData(CadCanvasToolMode.CircleCenterRadius, "0,0", "0,0", "10,10")]
    [InlineData(CadCanvasToolMode.CircleCenterDiameter, "0,0", "0,0", "10,10")]
    [InlineData(CadCanvasToolMode.CircleTwoPoint, "0,0", "0,0", "10,10")]
    [InlineData(CadCanvasToolMode.CircleThreePoint, "0,0|10,0", "20,0", "0,10")]
    [InlineData(CadCanvasToolMode.ArcThreePoint, "0,0|10,0", "20,0", "0,10")]
    [InlineData(CadCanvasToolMode.ArcCenterStartEnd, "0,0|10,0", "20,0", "0,10")]
    [InlineData(CadCanvasToolMode.EllipseCenter, "0,0|20,0", "10,0", "10,10")]
    [InlineData(CadCanvasToolMode.EllipseAxisEnd, "0,0|20,0", "10,0", "10,10")]
    [InlineData(CadCanvasToolMode.EllipseArc, "0,0|20,0|10,10|20,0", "20,0", "10,10")]
    public void RejectedPointKeepsStepInputPreviousPointsAndHistory(
        CadCanvasToolMode mode, string previous, string invalid, string corrected)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetToolMode(mode);
        foreach (var point in previous.Split('|')) Submit(vm, point);
        var prompt = vm.CurrentStepPrompt;
        var lastPoint = ((ICadCommandLineContext)vm).LastInputPoint;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();

        vm.StepInput = invalid;
        vm.SubmitStepInputCommand.Execute(null);
        Assert.NotEmpty(vm.StepInputError);
        Assert.Equal(invalid, vm.StepInput);
        Assert.Equal(prompt, vm.CurrentStepPrompt);
        Assert.Equal(lastPoint, ((ICadCommandLineContext)vm).LastInputPoint);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Empty(vm.CadEditor.Document.Entities);

        Submit(vm, corrected);
        var entity = Assert.Single(vm.CadEditor.Document.Entities.Values);
        Assert.False(entity.IsErased);
        Assert.False(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Undo();
        Assert.True(entity.IsErased);
        vm.Redo();
        Assert.False(entity.IsErased);
    }

    [Fact]
    public void TerminalRejectsCollinearCircleAndAllowsCorrectedThirdPoint()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var terminal = new CadCommandLineService();
        foreach (var input in new[] { "CIRCLE 3P", "0,0", "10,0" })
            Assert.True(terminal.Execute(input, vm).Success);
        var rejected = terminal.Execute("20,0", vm);
        Assert.False(rejected.Success);
        Assert.Contains(vm.StepInputError, rejected.Message);
        Assert.Equal(new CadCommandLinePoint(10, 0), ((ICadCommandLineContext)vm).LastInputPoint);
        Assert.True(terminal.Execute("@-10,10", vm).Success);
        var circle = Assert.IsType<CadCircle>(Assert.Single(vm.CadEditor.Document.Entities.Values));
        Assert.Equal(new CadPointD(5, 5), circle.Center);
    }

    [Fact]
    public void DegenerateEllipseAxisCanBeRetriedBeforeChoosingSecondRadius()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetToolMode(CadCanvasToolMode.EllipseCenter);
        Submit(vm, "0,0");
        vm.StepInput = "0,0";
        vm.SubmitStepInputCommand.Execute(null);
        Assert.NotEmpty(vm.StepInputError);
        Submit(vm, "20,0");
        Submit(vm, "0,10");
        Assert.IsType<CadEllipse>(Assert.Single(vm.CadEditor.Document.Entities.Values));
    }

    [Fact]
    public void FailedEditReportsFailureToTerminalAndKeepsParameters()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var circle = vm.CadEditor.AddCircle(default, 2);
        vm.SelectEntities([circle]);
        vm.SetToolMode(CadCanvasToolMode.Offset);
        vm.EditDistance = 3;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(new CadCommandLineService().Execute("0,0", vm).Success);
        Assert.NotEmpty(vm.StepInputError);
        Assert.Equal(3, vm.EditDistance);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    private static void Submit(CadDocumentViewModel vm, string point)
    {
        vm.StepInput = point;
        vm.SubmitStepInputCommand.Execute(null);
        Assert.Empty(vm.StepInputError);
        Assert.Empty(vm.StepInput);
    }
}
