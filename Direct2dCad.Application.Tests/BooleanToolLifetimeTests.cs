using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;

namespace Direct2dCad.Application.Tests;

public sealed class BooleanToolLifetimeTests
{
    [Fact]
    public async Task ReplacingEditorWhileOperationWaitsRejectsCommitWithoutTouchingEitherDocument()
    {
        using var workspace = new WaitingWorkspace();
        var original = workspace.Host.Session.CadEditor;
        var originalVersion = original.DocumentChangeVersion;
        var pending = ExecuteBoolean(workspace);
        await workspace.Host.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        workspace.Host.Load(CreateOperands("Replacement"), string.Empty);
        var replacement = workspace.Host.Session.CadEditor;
        var replacementVersion = replacement.DocumentChangeVersion;
        var changes = 0;
        original.DocumentChanged += (_, _) => changes++;
        replacement.DocumentChanged += (_, _) => changes++;
        workspace.Host.ContinueOperation.SetResult();

        using var result = JsonDocument.Parse(await pending);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("document changed", result.RootElement.GetProperty("error").GetString());
        Assert.Equal(0, changes);
        AssertUnchanged(original, originalVersion);
        AssertUnchanged(replacement, replacementVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingSessionWhileOperationWaitsCancelsWithoutChangingDocument(bool removeFromWorkspace)
    {
        using var workspace = new WaitingWorkspace();
        var editor = workspace.Host.Session.CadEditor;
        var version = editor.DocumentChangeVersion;
        var pending = ExecuteBoolean(workspace);
        await workspace.Host.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        workspace.Host.Dispose();
        workspace.IsOpen = !removeFromWorkspace;
        workspace.Host.ContinueOperation.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        AssertUnchanged(editor, version);
    }

    [Fact]
    public async Task UnchangedSessionCommitsPreparedBooleanAfterWaiting()
    {
        using var workspace = new WaitingWorkspace();
        var pending = ExecuteBoolean(workspace);
        await workspace.Host.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        workspace.Host.ContinueOperation.SetResult();

        using var result = JsonDocument.Parse(await pending);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        var editor = workspace.Host.Session.CadEditor;
        var region = Assert.IsType<CadRegion>(Assert.Single(editor.Document.Entities.Values, entity => !entity.IsErased));
        Assert.Equal(Math.PI * 91, region.Area, 7);
        Assert.True(editor.DocumentCommands.CanUndo);
        editor.UndoDocument();
        Assert.Equal(2, editor.Document.Entities.Values.Count(entity => !entity.IsErased));
    }

    private static Task<string> ExecuteBoolean(WaitingWorkspace workspace)
    {
        var ids = workspace.Host.Session.CadEditor.Document.Entities.Keys.OrderBy(id => id.Value).ToArray();
        var args = JsonSerializer.Serialize(new
        {
            document_id = workspace.Document.DocumentId,
            operation = "difference",
            entity_ids = ids.Select(id => id.Value).ToArray(),
            subject_entity_id = ids[0].Value
        });
        return new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new AiToolCall("boolean", "boolean_regions", args), default);
    }

    private static CadDocument CreateOperands(string name)
    {
        var document = CadDocument.Create(name);
        document.AddCircle(default, 10);
        document.AddCircle(default, 3);
        return document;
    }

    private static void AssertUnchanged(CadEditor editor, long version)
    {
        Assert.Equal(version, editor.DocumentChangeVersion);
        Assert.Equal(2, editor.Document.Entities.Count);
        Assert.All(editor.Document.Entities.Values, entity =>
        {
            Assert.IsType<CadCircle>(entity);
            Assert.False(entity.IsErased);
        });
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.Empty(editor.Selection.EntityIds);
    }

    private sealed class WaitingWorkspace : ICadToolWorkspace, IDisposable
    {
        public WaitingDocument Host { get; } = new(CreateOperands("Original"));
        public bool IsOpen { get; set; } = true;
        public CadToolWorkspaceDocument Document => new("document", Host.Session.CadEditor.Document.Id.Value,
            "Document", string.Empty, false, true, Host);
        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => IsOpen ? [Document] : [];
        public CadToolWorkspaceDocument? GetActiveDocument() => IsOpen ? Document : null;
        public CadToolWorkspaceDocument GetRequiredDocument(string documentId) => GetDocuments().Single(d => d.DocumentId == documentId);
        public CadToolWorkspaceDocument CreateDocument(string? name) => throw new NotSupportedException();
        public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool ActivateDocument(string documentId) => throw new NotSupportedException();
        public bool RenameDocument(string documentId, string name) => throw new NotSupportedException();
        public Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CloseDocumentAsync(string documentId) => throw new NotSupportedException();
        public void Dispose() => Host.Dispose();
    }

    private sealed class WaitingDocument(CadDocument document) : ICadWorkspaceDocument, IDisposable
    {
        private readonly ReloadableSession _session = new(document);
        public ICadToolDocumentSession Session => _session;
        public TaskCompletionSource OperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueOperation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Load(CadDocument value, string filePath) => _session.Load(value);
        public async Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default)
        {
            OperationStarted.SetResult();
            await ContinueOperation.Task.WaitAsync(token);
            return await operation(token);
        }
        public void Dispose() => _session.Dispose();
    }

    // WPF keeps its document-session adapter while Load replaces that adapter's editor.
    private sealed class ReloadableSession(CadDocument document) : ICadToolDocumentSession, IDisposable
    {
        private CadToolDocumentSession _inner = new(document);
        public CadEditor CadEditor => _inner.CadEditor;
        public bool IsDisposed => _inner.IsDisposed;
        public LayerId DrawingLayerId { get => _inner.DrawingLayerId; set => _inner.DrawingLayerId = value; }
        public string ToolMode => _inner.ToolMode;
        public double CurrentPointerWorldX => _inner.CurrentPointerWorldX;
        public double CurrentPointerWorldY => _inner.CurrentPointerWorldY;
        public LayoutId? ActiveLayoutId => _inner.ActiveLayoutId;
        public LayoutViewportId? ActiveLayoutViewportId => _inner.ActiveLayoutViewportId;
        public BlockId? EditingBlockId => _inner.EditingBlockId;
        public string EditingBlockName => _inner.EditingBlockName;
        public bool IsEditingBlock => _inner.IsEditingBlock;
        public bool IsModelSpaceActive => _inner.IsModelSpaceActive;
        public bool IsLayoutViewportActive => _inner.IsLayoutViewportActive;
        public bool IsPaperSpaceActive => _inner.IsPaperSpaceActive;
        public void FitToWindow() => _inner.FitToWindow();
        public void RequestRender() => _inner.RequestRender();
        public void SelectEntities(IEnumerable<EntityId> entityIds) => _inner.SelectEntities(entityIds);
        public void EditBlockDefinition(BlockId blockId) => _inner.EditBlockDefinition(blockId);
        public void ExitBlockEditing() => _inner.ExitBlockEditing();
        public void Load(CadDocument value) { _inner.Dispose(); _inner = new(value); }
        public void Dispose() => _inner.Dispose();
    }
}
