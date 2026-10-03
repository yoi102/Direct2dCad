using Direct2dCad.Db.Cad;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Tests;

public sealed class RecoveryCloseWorkflowTests
{
    [Fact]
    public async Task OpeningTheSameRecoverySourceActivatesItsExistingEditedTab()
    {
        using var context = new MainWindowTestContext();
        var sourceId = Guid.NewGuid().ToString();
        // The missing snapshot proves that activating the existing tab does not reload it.
        var entry = new CadRecoveryEntry(sourceId, "Same name", "", DateTimeOffset.UtcNow, "missing.d2cad", 1);
        using var store = new CadRecoveryStore(Path.Combine(Path.GetTempPath(), "RecoveryReuse-" + Guid.NewGuid()));
        var recovered = context.AddDocument("Same name").Tab;
        recovered.AttachRecoverySource(store, entry, () => { });
        recovered.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 10));
        var other = context.AddDocument("Same name").Tab;
        Assert.Same(other, context.ViewModel.CurrentEditorTabViewModel);
        await context.ViewModel.OpenRecoveryCopyCommand.ExecuteAsync(entry with { SnapshotName = "new-version.d2cad", DocumentVersion = 2 });
        await context.ViewModel.OpenRecoveryCopyCommand.ExecuteAsync(entry);
        Assert.Same(recovered, context.ViewModel.CurrentEditorTabViewModel);
        Assert.Same(recovered, context.Layout.ActiveDockable);
        Assert.Equal(2, context.Layout.Documents.OfType<EditorTabViewModel>().Count());
        Assert.Single(recovered.CadDocumentViewModel.CadEditor.Document.Entities);
        Assert.False(context.ViewModel.IsRecoveryActionRunning);
    }

    [Theory]
    [InlineData(false, UnsavedDocumentDialogResult.Discard)]
    [InlineData(true, UnsavedDocumentDialogResult.Discard)]
    [InlineData(false, UnsavedDocumentDialogResult.Save)]
    [InlineData(true, UnsavedDocumentDialogResult.Save)]
    [InlineData(false, UnsavedDocumentDialogResult.Cancel)]
    [InlineData(true, UnsavedDocumentDialogResult.Cancel)]
    public async Task DocumentAndApplicationCloseRespectRecoveryChoice(bool application, UnsavedDocumentDialogResult choice)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecoveryClose-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(directory, trackSession: true);
            using var context = new MainWindowTestContext(store);
            context.Dialogs.CloseResult = choice;
            context.Files.SavePath = Path.Combine(directory, "saved.d2cad");
            var (tab, _) = context.AddDocument("Current");
            tab.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 10));
            await store.SaveAsync(tab.CadDocumentViewModel.CadEditor, "");
            await context.ViewModel.CaptureRecoveryCopiesAsync();
            var abandoned = new CadEditor(CadDocument.Create("Unrelated abandoned drawing"));
            abandoned.AddLine(default, new(20, 20));
            await new CadRecoveryStore(directory).SaveAsync(abandoned, "");
            context.ViewModel.RefreshRecoveryEntries();
            Assert.Equal(abandoned.Document.Id.ToString(), Assert.Single(context.ViewModel.RecoveryEntries).DocumentId);

            var closed = application ? await context.ViewModel.ConfirmCloseApplicationAsync() : await tab.ConfirmCloseAsync();
            Assert.Equal(choice != UnsavedDocumentDialogResult.Cancel, closed);
            Assert.Equal(choice == UnsavedDocumentDialogResult.Cancel ? 2 : 1, store.List().Count);
            Assert.Equal(abandoned.Document.Id.ToString(), Assert.Single(store.ListRecoverable()).DocumentId);
            // Cancellation keeps the tab eligible for subsequent backups.
            if (!closed) await store.SaveAsync(tab.CadDocumentViewModel.CadEditor, "");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedCloseSaveKeepsRecoveryCopiesAndDoesNotEndSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecoverySaveFailure-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(directory, trackSession: true);
            using var context = new MainWindowTestContext(store);
            context.Dialogs.CloseResult = UnsavedDocumentDialogResult.Save;
            context.Files.SavePath = Path.Combine(directory, "failed.d2cad");
            var (tab, writer) = context.AddDocument("Keep me");
            writer.Failure = new IOException("Disk full");
            tab.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 10));
            await store.SaveAsync(tab.CadDocumentViewModel.CadEditor, "");
            await context.ViewModel.CaptureRecoveryCopiesAsync();
            Assert.False(await context.ViewModel.ConfirmCloseApplicationAsync());
            Assert.Single(store.List());
            Assert.Empty(store.ListRecoverable());
            await store.SaveAsync(tab.CadDocumentViewModel.CadEditor, "");
            Assert.Equal(2, store.List().Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecoveryClearCommandsRemoveAllVersionsWithoutTouchingLiveBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecoveryCommands-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(directory, trackSession: true);
            using var context = new MainWindowTestContext(store);
            Assert.False(context.ViewModel.ClearAllRecoveryCommand.CanExecute(null));
            var seed = new CadRecoveryStore(directory);
            var first = new CadEditor(CadDocument.Create("First"));
            var second = new CadEditor(CadDocument.Create("Second"));
            first.AddLine(default, new(10, 10));
            second.AddLine(default, new(20, 20));
            await seed.SaveAsync(first, "");
            await seed.SaveAsync(first, "");
            await seed.SaveAsync(second, "");
            var (tab, _) = context.AddDocument("Live");
            tab.CadDocumentViewModel.CadEditor.AddLine(default, new(30, 30));
            await store.SaveAsync(tab.CadDocumentViewModel.CadEditor, "");
            context.ViewModel.RefreshRecoveryEntries();
            Assert.Equal(2, context.ViewModel.RecoveryEntries.Count);
            Assert.True(context.ViewModel.ClearAllRecoveryCommand.CanExecute(null));
            var entry = context.ViewModel.RecoveryEntries.Single(e => e.DocumentId == first.Document.Id.ToString());
            await context.ViewModel.ClearRecoveryEntryCommand.ExecuteAsync(entry);
            Assert.Equal(second.Document.Id.ToString(), Assert.Single(context.ViewModel.RecoveryEntries).DocumentId);
            Assert.Equal(2, seed.List().Count);
            await context.ViewModel.ClearAllRecoveryCommand.ExecuteAsync(null);
            Assert.Empty(context.ViewModel.RecoveryEntries);
            Assert.False(context.ViewModel.HasRecoveryEntries);
            Assert.False(context.ViewModel.ClearAllRecoveryCommand.CanExecute(null));
            Assert.Equal(tab.Id, Assert.Single(seed.List()).DocumentId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiscardWaitsForAnInFlightBackupAndPreventsLaterBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecoveryCloseRace-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(directory, trackSession: true);
            using var context = new MainWindowTestContext(store);
            context.Dialogs.CloseResult = UnsavedDocumentDialogResult.Discard;
            var (tab, _) = context.AddDocument("Pending");
            for (var i = 0; i < 1000; i++) tab.CadDocumentViewModel.CadEditor.AddLine(new(i, 0), new(i, 10));
            await Task.Delay(5100); // Exercise the real idle threshold before starting the snapshot.
            var writing = tab.TryWriteRecoveryAsync(store);
            Assert.False(writing.IsCompleted);
            Assert.True(await tab.ConfirmCloseAsync());
            await writing;
            await tab.TryWriteRecoveryAsync(store);
            Assert.Empty(store.List());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
