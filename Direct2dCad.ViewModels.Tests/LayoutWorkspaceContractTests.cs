using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Layouts;

namespace Direct2dCad.ViewModels.Tests;

public sealed class LayoutWorkspaceContractTests
{
    public static IEnumerable<object[]> InvalidDimensions =>
        from property in Enumerable.Range(0, 5)
        from value in new[] { 0.0, -1, double.NaN, double.PositiveInfinity }
        select new object[] { property, value };

    [Theory]
    [MemberData(nameof(InvalidDimensions))]
    public void InvalidPaperAndViewportDimensionsRestoreWithoutHistory(int property, double value)
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = tab.LayoutWorkspace;
        vm.SelectedTab = vm.Tabs[1];
        var before = Read();
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        switch (property)
        {
            case 0: vm.PaperWidth = value; break;
            case 1: vm.PaperHeight = value; break;
            case 2: vm.ViewportWidth = value; break;
            case 3: vm.ViewportHeight = value; break;
            case 4: vm.ViewportScale = value; break;
        }
        Assert.Equal(before, Read());
        Assert.NotEmpty(vm.ValidationError);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
        double Read() => property switch
        {
            0 => vm.PaperWidth, 1 => vm.PaperHeight, 2 => vm.ViewportWidth,
            3 => vm.ViewportHeight, _ => vm.ViewportScale
        };
    }

    [Fact]
    public void LayoutLifecycleAndLiveSettingsKeepTabsHistoryAndViewportOptionsConsistent()
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = tab.LayoutWorkspace;
        var editor = context.Document.CadEditor;
        Assert.True(vm.SelectedTab!.IsModelSpace);
        Assert.False(vm.CanDeleteLayout);
        vm.AddLayoutCommand.Execute(null);
        var id = vm.SelectedTab!.LayoutId!.Value;
        Assert.Equal(id, context.Document.ActiveLayoutId);
        Assert.True(vm.SettingsVisibility);
        Assert.True(vm.CanDeleteLayout);
        vm.SelectedTab.Name = "  Production  ";
        Assert.Equal("Production", editor.Document.GetLayout(id).Name);
        var name = vm.SelectedTab.Name;
        vm.SelectedTab.Name = " ";
        Assert.Equal(name, vm.SelectedTab.Name);
        var width = vm.PaperWidth;
        vm.PaperWidth = width + 10;
        vm.PaperWidth = width + 20;
        tab.UndoCommand.Execute(null);
        Assert.Equal(width, vm.PaperWidth);
        tab.RedoCommand.Execute(null);
        Assert.Equal(width + 20, vm.PaperWidth);
        var height = vm.PaperHeight;
        vm.SwapPaperOrientationCommand.Execute(null);
        Assert.Equal(height, vm.PaperWidth);
        Assert.Equal(width + 20, vm.PaperHeight);
        tab.UndoCommand.Execute(null);
        vm.PaperColor = CadColor.Red;
        Assert.Equal(CadColor.Red, editor.Document.GetLayout(id).PaperColor);
        vm.ModelCenterX = 15;
        vm.ModelCenterY = 25;
        vm.ViewportScale = 2;
        vm.ViewportRotationDegrees = 30;
        var viewport = editor.Document.GetLayout(id).GetViewport(vm.SelectedViewport!.Id);
        Assert.Equal(15, viewport.ModelCenter.X);
        Assert.Equal(25, viewport.ModelCenter.Y);
        Assert.Equal(Math.PI / 6, viewport.RotationRadians, 8);
        vm.CurrentViewport = vm.CurrentViewportOptions.Single();
        Assert.True(context.Document.IsLayoutViewportActive);
        vm.IsViewportLocked = true;
        Assert.True(viewport.IsLocked);
        vm.IsViewportVisible = false;
        Assert.False(context.Document.IsLayoutViewportActive);
        Assert.Empty(vm.CurrentViewportOptions);
        tab.UndoCommand.Execute(null);
        Assert.Single(vm.CurrentViewportOptions);
        vm.RemoveViewportCommand.Execute(null);
        Assert.Empty(editor.Document.GetLayout(id).Viewports);
        tab.UndoCommand.Execute(null);
        Assert.Single(vm.Viewports);
        vm.DeleteLayoutCommand.Execute(null);
        Assert.False(editor.Document.Layouts.ContainsKey(id));
        Assert.True(vm.SelectedTab!.IsModelSpace);
        tab.UndoCommand.Execute(null);
        Assert.Contains(vm.Tabs, item => item.LayoutId == id);
    }

    [Fact]
    public void ViewportCreationModePlacesPaperRectangleAndCancelsWithoutChanges()
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        var layout = vm.CadEditor.Document.GetLayout(vm.ActiveLayoutId!.Value);
        var count = layout.Viewports.Count;
        tab.LayoutWorkspace.AddViewportCommand.Execute(null);
        Assert.Equal(CadCanvasToolMode.LayoutViewport, vm.CadCanvasToolMode);
        Click(new(20, 20));
        Click(new(80, 70));
        Assert.Equal(count + 1, layout.Viewports.Count);
        Assert.Contains(tab.LayoutWorkspace.Viewports, item => item.Id == layout.Viewports[^1].Id);
        vm.Undo();
        Assert.Equal(count, layout.Viewports.Count);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        tab.LayoutWorkspace.AddViewportCommand.Execute(null);
        Click(new(20, 20));
        vm.Escape();
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        void Click(Direct2dCad.Db.Geometry.CadPointD point)
        {
            var screen = vm.CadEditor.Viewport.WorldToScreen(point);
            vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
            vm.PointerUp(screen, CadCanvasPointerButton.Left);
        }
    }
}
