using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadDocumentSaveSessionTests
{
    [Fact]
    public async Task SaveKeepsLaterEditsDirtyAndUndoReturnsToSavedState()
    {
        var editor = CreateEditor();
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(editor, writer);
        Assert.True(session.IsModified);
        var save = session.SaveAsync("one.d2cad");
        editor.AddLine(CadPointD.Origin, new(10, 10));
        writer.Complete();
        Assert.True(await save);
        Assert.True(session.IsModified);
        editor.Undo();
        Assert.False(session.IsModified);
        editor.Redo();
        Assert.True(session.IsModified);
    }

    [Fact]
    public async Task SavesAndPathCommitsAreSerializedAndCloseWaitsForThem()
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer);
        var paths = new List<string>();
        var first = session.SaveAsync("first.d2cad", () => paths.Add(session.FilePath));
        var second = session.SaveAsync("second.d2cad", () => paths.Add(session.FilePath));
        var idle = session.WaitForIdleAsync();
        Assert.Single(writer.Paths);
        Assert.False(idle.IsCompleted);
        writer.Complete();
        Assert.True(await first);
        await writer.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Complete();
        Assert.True(await second);
        await idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "first.d2cad", "second.d2cad" }, paths);
        Assert.Equal(paths, writer.Paths);
        Assert.False(session.IsModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedOrdinarySaveUsesLatestSuccessfullyCommittedPath(bool failSaveAs)
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer, "original.d2cad");
        var saveAs = session.SaveAsync("renamed.d2cad");
        var ordinarySave = session.SaveCurrentAsync();
        Assert.Single(writer.Paths);
        if (failSaveAs)
        {
            writer.Fail();
            await Assert.ThrowsAsync<IOException>(() => saveAs);
        }
        else
        {
            writer.Complete();
            Assert.True(await saveAs);
        }
        await writer.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Complete();
        Assert.True(await ordinarySave);
        var expectedPath = failSaveAs ? "original.d2cad" : "renamed.d2cad";
        Assert.Equal(new[] { "renamed.d2cad", expectedPath }, writer.Paths);
        Assert.Equal(expectedPath, session.FilePath);
        Assert.False(session.IsModified);
    }

    [Fact]
    public async Task InvalidArgumentsDoNotCancelAnExistingSaveOrInvokeWriter()
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveCurrentAsync());
        Assert.Empty(writer.Paths);
        var save = session.SaveAsync("valid.d2cad");
        Assert.Throws<ArgumentNullException>(() => session.Reset(null!, "other.d2cad"));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(" "));
        writer.Complete();
        Assert.True(await save);
        Assert.Equal("valid.d2cad", session.FilePath);
        Assert.Single(writer.Paths);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotInvokeWriterOrLoseBaseline()
    {
        var editor = CreateEditor();
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(editor, writer, "old.d2cad");
        var first = session.SaveAsync("first.d2cad");
        using var cancellation = new CancellationTokenSource();
        var second = session.SaveAsync("cancelled.d2cad", cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Single(writer.Paths);
        writer.Complete();
        await first;
        Assert.Equal("first.d2cad", session.FilePath);
    }

    [Fact]
    public async Task ReplacementCancelsOldSaveAndCannotMarkNewDocumentSaved()
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer);
        var committed = false;
        var save = session.SaveAsync("old.d2cad", () => committed = true);
        session.Reset(CreateEditor(), "loaded.d2cad");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        Assert.False(committed);
        Assert.Equal("loaded.d2cad", session.FilePath);
        Assert.False(session.IsModified);
    }

    [Fact]
    public async Task DisposalCancelsInFlightAndRejectsNewSaves()
    {
        var writer = new ControlledWriter();
        var session = new CadDocumentSaveSession(CreateEditor(), writer);
        var save = session.SaveAsync("old.d2cad");
        session.Dispose();
        session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SaveAsync("new.d2cad"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSuccessfulWriteCannotCommitAfterReplacementOrDisposal(bool dispose)
    {
        var writer = new ControlledWriter(honorCancellation: false);
        using var session = new CadDocumentSaveSession(CreateEditor(), writer);
        var committed = false;
        var save = session.SaveAsync("old.d2cad", () => committed = true);
        if (dispose)
            session.Dispose();
        else
            session.Reset(CreateEditor(), "replacement.d2cad");
        writer.Complete();
        Assert.False(await save);
        Assert.False(committed);
        Assert.Equal(dispose ? "" : "replacement.d2cad", session.FilePath);
    }

    [Fact]
    public async Task DirectChangesDuringWriteStayDirty()
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer, "old.d2cad");
        var save = session.SaveAsync("old.d2cad");
        session.MarkDirectChange();
        writer.Complete();
        await save;
        Assert.True(session.IsModified);
    }

    [Fact]
    public async Task ChangedSnapshotsRetryWithFreshStateButDoNotRetryForever()
    {
        var editor = CreateEditor();
        var writer = new ChangingWriter(editor, 1);
        using var session = new CadDocumentSaveSession(editor, writer);
        Assert.True(await session.SaveAsync("new.d2cad"));
        Assert.Equal(2, writer.Calls);
        Assert.False(session.IsModified);

        writer.RemainingChanges = 10;
        await Assert.ThrowsAsync<CadSnapshotChangedException>(() => session.SaveAsync("other.d2cad"));
        Assert.Equal(5, writer.Calls);
        Assert.Equal("new.d2cad", session.FilePath);
        Assert.True(session.IsModified);
    }

    [Fact]
    public async Task FailedSaveReleasesGateAndDoesNotCommitPath()
    {
        var writer = new ControlledWriter();
        using var session = new CadDocumentSaveSession(CreateEditor(), writer);
        var failed = session.SaveAsync("bad.d2cad");
        writer.Fail();
        await Assert.ThrowsAsync<IOException>(() => failed);
        Assert.Equal("", session.FilePath);
        Assert.True(session.IsModified);
        var retry = session.SaveAsync("good.d2cad");
        writer.Complete();
        Assert.True(await retry);
        Assert.False(session.IsModified);
    }

    private static CadEditor CreateEditor() => new(CadDocument.Create("Save"));

    private sealed class ControlledWriter(bool honorCancellation = true) : ICadDocumentWriter
    {
        private TaskCompletionSource _completion = NewCompletion();
        public List<string> Paths { get; } = [];
        public TaskCompletionSource SecondStarted { get; } = NewCompletion();
        public Task SaveAsync(CadDocument document, string filePath, CadSnapshotCaptureOptions capture, CancellationToken cancellationToken)
        {
            Paths.Add(filePath);
            _completion = NewCompletion();
            if (Paths.Count == 2) SecondStarted.TrySetResult();
            return honorCancellation ? _completion.Task.WaitAsync(cancellationToken) : _completion.Task;
        }
        public void Complete() => _completion.TrySetResult();
        public void Fail() => _completion.TrySetException(new IOException("Write failed"));
        private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ChangingWriter(CadEditor editor, int remainingChanges) : ICadDocumentWriter
    {
        public int RemainingChanges = remainingChanges;
        public int Calls;
        public Task SaveAsync(CadDocument document, string filePath, CadSnapshotCaptureOptions capture, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.True(capture.IsCurrent());
            if (RemainingChanges-- > 0)
            {
                editor.AddLine(CadPointD.Origin, new(Calls, 1));
                Assert.False(capture.IsCurrent());
                throw new CadSnapshotChangedException();
            }
            return Task.CompletedTask;
        }
    }
}
