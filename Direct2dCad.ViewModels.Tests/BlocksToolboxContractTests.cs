using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tests;

public sealed class BlocksToolboxContractTests
{
    [Fact]
    public async Task BlockPanelRenameInsertionEditingAndDeletionAreUndoable()
    {
        using var context = new CadToolboxTestContext();
        var dialogs = new RecordingDialogService();
        using var vm = context.CreateBlocks(dialogs);
        vm.IsOpen = true;
        var editor = context.Document.CadEditor;
        var block = editor.Document.CreateBlockDefinition("Original", CadPointD.Origin);
        vm.Attach(context.Document);
        Assert.Single(vm.Blocks);
        vm.SelectedBlock = vm.Blocks[0];
        vm.SelectedBlock.Name = "Renamed";
        Assert.Equal("Renamed", editor.Document.GetBlock(block).Name);
        editor.Undo();
        context.Publish();
        Assert.Equal("Original", vm.Blocks[0].Name);
        vm.SelectedBlock = vm.Blocks[0];
        vm.InsertCommand.Execute(null);
        Assert.Equal(Enums.CadCanvasToolMode.InsertBlock, context.Document.CadCanvasToolMode);
        context.Document.Escape();
        vm.SelectedBlock = vm.Blocks[0];
        vm.EditCommand.Execute(null);
        Assert.True(vm.IsEditingBlock);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        vm.ExitEditCommand.Execute(null);
        Assert.False(vm.IsEditingBlock);
        vm.SelectedBlock = vm.Blocks[0];
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.True(editor.Document.Blocks.ContainsKey(block));
        dialogs.ConfirmationResult = true;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.False(editor.Document.Blocks.ContainsKey(block));
        editor.Undo();
        context.Publish();
        Assert.Single(vm.Blocks);
        vm.SelectedBlock = vm.Blocks[0];
        vm.IsOpen = true;
        vm.IsOpen = false;
        Assert.Null(vm.SelectedBlock);
        vm.Attach(null);
        Assert.False(vm.HasDocument);
        Assert.Empty(vm.Blocks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PendingDeleteCannotDeleteSameIdInANewlyAttachedDocument(bool switchBack, bool detach)
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        var a = first.Document.CadEditor.Document.CreateBlockDefinition("First", CadPointD.Origin);
        var b = second.Document.CadEditor.Document.CreateBlockDefinition("Second", CadPointD.Origin);
        Assert.Equal(a, b);
        var completion = new TaskCompletionSource<bool>();
        using var vm = first.CreateBlocks(new() { PendingConfirmation = completion.Task });
        vm.Attach(first.Document);
        vm.SelectedBlock = vm.Blocks[0];
        var deletion = vm.DeleteCommand.ExecuteAsync(null);
        vm.Attach(detach ? null : second.Document);
        if (switchBack)
            vm.Attach(first.Document);
        completion.SetResult(true);
        await deletion;
        Assert.True(second.Document.CadEditor.Document.Blocks.ContainsKey(b));
        Assert.True(first.Document.CadEditor.Document.Blocks.ContainsKey(a));
    }

    [Fact]
    public void StaleItemCannotRenameABlockInAnotherDocument()
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        first.Document.CadEditor.Document.CreateBlockDefinition("First", CadPointD.Origin);
        var id = second.Document.CadEditor.Document.CreateBlockDefinition("Second", CadPointD.Origin);
        using var vm = first.CreateBlocks(new());
        vm.Attach(first.Document);
        var item = Assert.Single(vm.Blocks);
        vm.Attach(second.Document);
        item.Name = "Stale edit";
        Assert.Equal("Second", second.Document.CadEditor.Document.GetBlock(id).Name);
    }

    [Fact]
    public void InteractionAndUnrelatedEntityEditsKeepBlockItemsAndCollectionStable()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        editor.Document.CreateBlockDefinition("A", CadPointD.Origin);
        using var vm = context.CreateBlocks(new());
        vm.IsOpen = true;
        vm.Attach(context.Document);
        var item = Assert.Single(vm.Blocks);
        vm.SelectedBlock = item;
        var changes = 0;
        vm.Blocks.CollectionChanged += (_, _) => changes++;
        for (var index = 0; index < 100; index++)
        {
            editor.AddLine(new(index, 0), new(index, 10));
            context.Publish();
        }
        Assert.Same(item, vm.SelectedBlock);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RenameReordersExistingItemsAndClosedPanelDefersRefresh()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var a = editor.Document.CreateBlockDefinition("A", CadPointD.Origin);
        editor.Document.CreateBlockDefinition("B", CadPointD.Origin);
        using var vm = context.CreateBlocks(new());
        vm.IsOpen = true;
        vm.Attach(context.Document);
        var item = vm.Blocks[0];
        vm.SelectedBlock = item;
        editor.RenameBlock(a, "Z");
        context.Publish();
        Assert.Same(item, vm.Blocks[1]);
        Assert.Same(item, vm.SelectedBlock);
        Assert.Equal("Z", item.Name);
        vm.IsOpen = false;
        editor.Undo();
        context.Publish();
        Assert.Equal("Z", item.Name);
        vm.IsOpen = true;
        Assert.Same(item, vm.Blocks[0]);
        Assert.Equal("A", item.Name);
    }
}
