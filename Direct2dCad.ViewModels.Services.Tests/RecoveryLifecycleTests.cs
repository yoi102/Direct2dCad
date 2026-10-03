using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class RecoveryLifecycleTests
{
    [Fact]
    public async Task LiveSessionsAreHiddenAndOnlyAbandonedCopiesCanBeCleared()
    {
        var path = Path.Combine(Path.GetTempPath(), "RecoverySessions-" + Guid.NewGuid());
        try
        {
            using var live = new CadRecoveryStore(path, trackSession: true);
            using var abandoned = new CadRecoveryStore(path, trackSession: true);
            var editor = new CadEditor(CadDocument.Create("Abandoned"));
            editor.AddLine(default, new(10, 0));
            await abandoned.SaveAsync(editor, "original.d2cad");
            editor.AddLine(default, new(20, 0));
            await abandoned.SaveAsync(editor, "original.d2cad");
            await live.SaveAsync(editor, "original.d2cad");
            var observer = new CadRecoveryStore(path);
            Assert.Empty(observer.ListRecoverable());
            await observer.RemoveDocumentAsync(editor.Document.Id.ToString());
            Assert.Equal(3, observer.List().Count);

            abandoned.Dispose(); // The OS also releases this lease when its process terminates.
            Assert.Equal(2, observer.ListRecoverable().Count);
            // Saving or closing the same original in another session must not remove its abandoned copies.
            await live.RemoveDocumentAsync(editor.Document.Id.ToString());
            Assert.Equal(2, observer.ListRecoverable().Count);
            await live.SaveAsync(editor, "original.d2cad");
            await observer.RemoveRecoverableAsync();
            Assert.Empty(observer.ListRecoverable());
            Assert.Single(observer.List());
            await live.CompleteSessionAsync();
            Assert.Empty(observer.List());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => live.SaveAsync(editor, ""));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task ClearingOneDrawingRemovesAllItsVersionsAndPreservesOriginalFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "RecoveryClear-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        try
        {
            var original = Path.Combine(path, "original.d2cad");
            await File.WriteAllTextAsync(original, "original contents");
            var store = new CadRecoveryStore(Path.Combine(path, "Recovery"));
            var first = new CadEditor(CadDocument.Create("First"));
            first.AddLine(default, new(10, 0));
            await store.SaveAsync(first, original);
            first.AddLine(default, new(20, 0));
            await store.SaveAsync(first, original);
            var second = new CadEditor(CadDocument.Create("Second"));
            second.AddLine(default, new(30, 0));
            await store.SaveAsync(second, "");
            await store.RemoveRecoverableAsync(first.Document.Id.ToString());
            Assert.Equal(second.Document.Id.ToString(), Assert.Single(store.List()).DocumentId);
            await store.RemoveRecoverableAsync();
            Assert.Empty(Directory.GetFiles(store.DirectoryPath, "*.d2cad"));
            Assert.Equal("original contents", await File.ReadAllTextAsync(original));
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task CleanSessionCompletionWaitsForPendingSnapshotAndLeavesNoRecoveryCopy()
    {
        var path = Path.Combine(Path.GetTempPath(), "RecoveryPending-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(path, trackSession: true);
            var editor = new CadEditor(CadDocument.Create("Pending"));
            for (var i = 0; i < 1000; i++) editor.AddLine(new(i, 0), new(i, 10));
            var writing = store.SaveAsync(editor, "");
            Assert.False(writing.IsCompleted);
            var closing = store.CompleteSessionAsync();
            await Task.WhenAll(writing, closing);
            Assert.Empty(store.ListRecoverable());
            Assert.Empty(Directory.GetFiles(path));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]public async Task OversizeCancelledAndAbandonedSnapshotsHaveNoRecoveryEntry()
    {
        var path=Path.Combine(Path.GetTempPath(),"RecoveryBudget-"+Guid.NewGuid());Directory.CreateDirectory(path);
        try
        {
            var e=new CadEditor(CadDocument.Create("recovery"));e.AddLine(default,new(10,0));var store=new CadRecoveryStore(path){MaximumBytes=1};
            await Assert.ThrowsAsync<IOException>(()=>store.SaveAsync(e,""));Assert.Empty(store.List());Assert.Empty(Directory.GetFiles(path));
            using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.SaveAsync(e,"",cancel.Token));Assert.Empty(Directory.GetFiles(path));
            foreach(var name in new[]{"abandoned.d2cad","abandoned.tmp","invalid.recovery.json"}){var file=Path.Combine(path,name);File.WriteAllText(file,"invalid");File.SetLastWriteTimeUtc(file,DateTime.UtcNow-TimeSpan.FromHours(2));}
            Assert.Empty(store.List());Assert.Empty(Directory.GetFiles(path));
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact]public async Task SuccessfulRecoveryCanBeDiscardedByDocumentIdentity()
    {
        var path=Path.Combine(Path.GetTempPath(),"RecoveryDiscard-"+Guid.NewGuid());
        try{var e=new CadEditor(CadDocument.Create("discard"));e.AddLine(default,new(10,0));var store=new CadRecoveryStore(path);await store.SaveAsync(e,"");store.RemoveDocument(e.Document.Id.ToString());Assert.Empty(store.List());Assert.Empty(Directory.GetFiles(path));}
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [Fact]public async Task DelayedBusySurfaceAndDisposalAreBoundToTheOperation()
    {
        var operation=new CadLongOperation();Assert.False(operation.CancelCommand.CanExecute(null));
        Assert.Equal(1,await operation.RunAsync("short",ct=>Task.FromResult(1)));Assert.False(operation.IsVisible);
        var pending=operation.RunAsync("long",async ct=>{await Task.Delay(5000,ct);return 2;});Assert.True(operation.IsRunning);
        await Task.Delay(350);Assert.True(operation.IsVisible);operation.Dispose();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await pending);
        Assert.False(operation.IsRunning);Assert.False(operation.IsVisible);Assert.False(operation.CancelCommand.CanExecute(null));await Assert.ThrowsAsync<ObjectDisposedException>(()=>operation.RunAsync("late",ct=>Task.FromResult(1)));
    }
}
