using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DrawingRecoveryToolboxTests
{
    [Fact]
    public void ActiveDocumentSwitchPreservesEachDocumentsDrawingState()
    {
        using var context = new MainWindowTestContext();
        var main = context.ViewModel;
        var first = context.AddDocument("First").Tab;
        first.CadDocumentViewModel.SetToolMode(CadCanvasToolMode.Line);
        first.CadDocumentViewModel.StepInput = "1.25,2.75";
        first.CadDocumentViewModel.IsGridSnapEnabled = true;
        var second = context.AddDocument("Second").Tab;
        second.CadDocumentViewModel.SetToolMode(CadCanvasToolMode.CircleCenterRadius);
        Assert.Same(main, main.DrawingRecovery.Workspace);
        Assert.Same(second, main.DrawingRecovery.Workspace!.CurrentEditorTabViewModel);
        Assert.False(second.CadDocumentViewModel.IsGridSnapEnabled);
        Assert.Empty(second.CadDocumentViewModel.StepInput);

        main.ActiveDockContent = first;
        Assert.Same(first, main.DrawingRecovery.Workspace.CurrentEditorTabViewModel);
        Assert.Equal("1.25,2.75", first.CadDocumentViewModel.StepInput);
        Assert.True(first.CadDocumentViewModel.IsGridSnapEnabled);
        Assert.Equal(CadCanvasToolMode.Line, first.CadDocumentViewModel.CadCanvasToolMode);
        main.DrawingRecovery.IsOpen = false;
        main.ShowDrawingRecoveryCommand.Execute(null);
        Assert.True(main.DrawingRecovery.IsOpen);
        Assert.Same(first, main.CurrentEditorTabViewModel);
        Assert.Equal("1.25,2.75", first.CadDocumentViewModel.StepInput);
    }
}
