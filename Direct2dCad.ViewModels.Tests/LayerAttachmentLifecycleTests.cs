using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using MessagePipe;

namespace Direct2dCad.ViewModels.Tests;

public sealed class LayerAttachmentLifecycleTests
{
    [Theory]
    [InlineData("switch")]
    [InlineData("switch_back")]
    [InlineData("detach")]
    [InlineData("dispose")]
    public async Task PendingConfirmationCannotOutliveItsDocumentAttachment(string action)
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        var a = first.Document.CadEditor.Document.CreateLayer("First", CadColor.Red, CadLineWeight.Default);
        var b = second.Document.CadEditor.Document.CreateLayer("Second", CadColor.Blue, CadLineWeight.Default);
        Assert.Equal(a, b);
        var pending = new TaskCompletionSource<bool>();
        using var vm = Create(first, new() { PendingConfirmation = pending.Task });
        vm.Attach(first.Document);
        vm.SelectedLayer = vm.Layers.Single(layer => layer.LayerId == a);
        var task = vm.DeleteSelectedLayerCommand.ExecuteAsync(null);
        switch (action)
        {
            case "switch": vm.Attach(second.Document); break;
            case "switch_back": vm.Attach(second.Document); vm.Attach(first.Document); break;
            case "detach": vm.Attach(null); break;
            case "dispose": vm.Dispose(); break;
        }
        pending.SetResult(true);
        await task;
        Assert.True(first.Document.CadEditor.Document.Layers.ContainsKey(a));
        Assert.True(second.Document.CadEditor.Document.Layers.ContainsKey(b));
    }

    [Fact]
    public void OldLayerItemsCannotEditAnotherDocumentsMatchingId()
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        using var vm = Create(first, new());
        vm.Attach(first.Document);
        var stale = Assert.Single(vm.Layers);
        vm.Attach(second.Document);
        var history = second.Document.CadEditor.CreateDocumentHistorySnapshot();
        var layer = Assert.Single(second.Document.CadEditor.Document.Layers.Values);
        var name = layer.Name;
        var color = layer.Color;
        stale.Name = "Wrong document";
        stale.Color = CadColor.Red;
        stale.IsLocked = true;
        stale.Priority = 42;
        Assert.True(second.Document.CadEditor.DocumentHistoryEquals(history));
        Assert.Equal(name, layer.Name);
        Assert.Equal(color, layer.Color);
        Assert.False(layer.IsLocked);
    }

    private static LayersToolboxViewModel Create(CadToolboxTestContext context, RecordingDialogService dialogs) =>
        new(context.Platform, dialogs, context.Platform, context.Platform,
            context.GetService<ISubscriber<CadDocumentInteractionStateChangedMessage>>());
}
