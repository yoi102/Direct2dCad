using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;

namespace Direct2dCad.ViewModels.Tests;

public sealed class BooleanCompletionContractTests
{
    [Fact]
    public Task EmptyIntersectionKeepsItsErrorAndDoneFailsWithoutChangingOriginals() => CadViewModelTestThread.RunAsync(async () =>
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var first = vm.CadEditor.AddCircle(default, 3);
        var second = vm.CadEditor.AddCircle(new(100, 0), 3);
        vm.SelectEntities([first, second]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        await vm.BeginBoolean(CadBooleanOperation.Intersection);
        Assert.Equal(CadUiText.Get("BooleanEmpty"), vm.StepInputError);

        Assert.True(vm.CompleteCurrentDrawing().Handled); // Canvas consumes Enter inside this tool.
        var done = new CadCommandLineService().Execute("DONE", vm);
        Assert.False(done.Success);
        Assert.Equal(CadUiText.Get("BooleanEmpty"), vm.StepInputError);
        Assert.True(vm.IsBooleanTool);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Equal(2, vm.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        Assert.DoesNotContain(vm.CadEditor.Document.Entities.Values, entity => entity is CadRegion);
    });

    [Fact]
    public Task DoneDuringActualPreparationFailsThenSucceedsAfterPreviewIsReady() => CadViewModelTestThread.RunAsync(async () =>
    {
        var uiThreadId = Environment.CurrentManagedThreadId;
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var first = vm.CadEditor.AddCircle(default, 10);
        var second = vm.CadEditor.AddCircle(new(10, 0), 10);
        vm.SelectEntities([first, second]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var commands = new CadCommandLineService();
        CadCommandLineResult? whileCalculating = null;
        bool? canvasHandled = null;
        var checkedPendingState = false;
        vm.PropertyChanged += (_, args) =>
        {
            Assert.Equal(uiThreadId, Environment.CurrentManagedThreadId);
            if (args.PropertyName != nameof(vm.CurrentStepPrompt) || !vm.IsBooleanCalculating || checkedPendingState)
                return;
            // Observe the real preparation lifecycle before its worker starts, without timing assumptions.
            checkedPendingState = true;
            canvasHandled = vm.CompleteCurrentDrawing().Handled;
            whileCalculating = commands.Execute("DONE", vm);
            Assert.Equal(CadUiText.Get("BooleanCalculating"), vm.StepInputError);
            Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        };

        await vm.BeginBoolean(CadBooleanOperation.Union);
        Assert.Equal(uiThreadId, Environment.CurrentManagedThreadId);
        Assert.True(checkedPendingState);
        Assert.True(canvasHandled);
        Assert.NotNull(whileCalculating);
        Assert.False(whileCalculating.Success);
        Assert.False(vm.IsBooleanCalculating);
        Assert.Empty(vm.StepInputError);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(commands.Execute("DONE", vm).Success);
        Assert.IsType<CadRegion>(Assert.Single(vm.CadEditor.Document.Entities.Values, entity => !entity.IsErased));
        vm.Undo();
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    });

    [Fact]
    public Task DifferenceWithoutSubjectConsumesCanvasEnterButDoneReportsFailure() => CadViewModelTestThread.RunAsync(async () =>
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var first = vm.CadEditor.AddCircle(default, 10);
        var second = vm.CadEditor.AddCircle(default, 5);
        vm.SelectEntities([first, second]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        await vm.BeginBoolean(CadBooleanOperation.Difference);
        Assert.True(vm.CompleteCurrentDrawing().Handled);
        Assert.False(new CadCommandLineService().Execute("DONE", vm).Success);
        Assert.Equal(CadUiText.Get("BooleanChooseSubject"), vm.StepInputError);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    });
}
