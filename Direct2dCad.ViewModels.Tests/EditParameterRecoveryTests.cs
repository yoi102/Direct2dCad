using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EditParameterRecoveryTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.Offset, 10)]
    [InlineData(CadCanvasToolMode.Offset, 20)]
    [InlineData(CadCanvasToolMode.Fillet, 10)]
    [InlineData(CadCanvasToolMode.Chamfer, 10)]
    public void TerminalDistanceRepairsInvalidTextEvenWhenValueIsUnchanged(CadCanvasToolMode mode, double distance)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(mode);
        vm.EditDistance = 10;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DynamicInputFields[0].Text = "bad";
        Assert.False(vm.SubmitDynamicInput());
        Assert.NotEmpty(vm.DynamicInputError);

        Assert.True(((ICadCommandLineContext)vm).SubmitScalarInput(distance));

        Assert.Equal(distance, vm.EditDistance);
        Assert.DoesNotContain("bad", vm.DynamicInputFields[0].Text);
        Assert.Empty(vm.DynamicInputError);
        Assert.Empty(vm.EditFeedback);
        Assert.True(vm.SubmitDynamicInput());
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e-15)]
    public void InvalidTerminalDistanceDoesNotDiscardUncorrectedText(double distance)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetToolMode(CadCanvasToolMode.Offset);
        vm.EditDistance = 10;
        vm.DynamicInputFields[0].Text = "bad";
        Assert.False(((ICadCommandLineContext)vm).SubmitScalarInput(distance));
        Assert.Equal(10, vm.EditDistance);
        Assert.Equal("bad", vm.DynamicInputFields[0].Text);
        Assert.False(vm.SubmitDynamicInput());
    }

    [Fact]
    public void RepairingFirstChamferDistancePreservesInvalidSecondDistance()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetToolMode(CadCanvasToolMode.Chamfer);
        vm.DynamicInputFields[1].Text = "bad second distance";
        Assert.False(((ICadCommandLineContext)vm).SubmitScalarInput(20));
        Assert.Equal("bad second distance", vm.DynamicInputFields[1].Text);
        Assert.NotEmpty(vm.DynamicInputError);
        vm.DynamicInputFields[1].Text = "30";
        Assert.True(vm.SubmitDynamicInput());
        Assert.Equal(20, vm.EditDistance);
        Assert.Equal(30, vm.EditSecondDistance);
    }

    [Fact]
    public void RecoveredTerminalOffsetUsesDocumentUnitsAndStaysLockedAcrossPointerMoves()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetDocumentUnit(CadUnit.Inch);
        var source = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([source]);
        vm.SetToolMode(CadCanvasToolMode.Offset);
        vm.EditDistance = 1;
        vm.DynamicInputFields[0].Text = "bad";
        Assert.True(new CadCommandLineService().Execute("1", vm).Success);
        var screen = vm.CadEditor.Viewport.WorldToScreen(new CadPointD(50, 80));
        vm.PointerMove(screen);
        Assert.Equal(1, vm.EditDistance);
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
        vm.PointerUp(screen, CadCanvasPointerButton.Left);
        var result = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadLine>(), line => line.Id != source && !line.IsErased);
        Assert.Equal(25.4, result.Start.Y, 8);
        vm.Undo();
        Assert.True(result.IsErased);
    }
}
