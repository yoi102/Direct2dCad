using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task ToolsEditTwoHeadlessDocumentsWithIndependentUndoAndQueries()
    {
        using var workspace = new HeadlessWorkspace();
        var one = workspace.CreateDocument("One");
        var executor = new CadWorkspaceToolExecutor(workspace);
        var two = workspace.CreateDocument("Two");
        await Execute(executor, "add_line", new { document_id = one.DocumentId, x1 = 0, y1 = 0, x2 = 10, y2 = 0 });
        await Execute(executor, "add_circle", new { document_id = two.DocumentId, center_x = 20, center_y = 10, radius = 4 });
        Assert.Single(one.Session.CadEditor.Document.Entities.Values, e => !e.IsErased);
        Assert.Single(two.Session.CadEditor.Document.Entities.Values, e => !e.IsErased);

        await Execute(executor, "undo", new { document_id = one.DocumentId });
        Assert.DoesNotContain(one.Session.CadEditor.Document.Entities.Values, e => !e.IsErased);
        Assert.Single(two.Session.CadEditor.Document.Entities.Values, e => !e.IsErased);
        await Execute(executor, "redo", new { document_id = one.DocumentId });
        var page = await Execute(executor, "list_entities", new { document_id = one.DocumentId, limit = 10 });
        Assert.Contains("Line", page.ToString());
        Assert.True(one.Session.CadEditor.DocumentCommands.CanUndo);
    }

    [Fact]
    public async Task HeadlessQueryRejectsStaleVersionAndCancellation()
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Query");
        var executor = new CadWorkspaceToolExecutor(workspace);
        document.Session.CadEditor.AddLine(default, new(10, 0));
        var version = document.Session.CadEditor.DocumentChangeVersion;
        document.Session.CadEditor.AddLine(default, new(20, 0));
        await Execute(executor, "list_entities", new { expected_document_version = version }, success: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new("cancel", "list_entities", "{}"), cancellation.Token));
    }

    [Fact]
    public void DisposedSessionCannotBeMutatedThroughRetainedExecutor()
    {
        using var session = new CadToolDocumentSession(CadDocument.Create("Closed"));
        var executor = new CadDocumentToolExecutor(session, Guid.NewGuid());
        session.Dispose();
        using var result = JsonDocument.Parse(executor.Execute(new("closed", "add_line", """{"x1":0,"y1":0,"x2":10,"y2":0}""")));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(session.CadEditor.Document.Entities);
    }

    [Fact]
    public async Task ReadOnlyBlockCannotBeEnteredByAHeadlessTool()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Protected block").Session;
        var document = session.CadEditor.Document;
        var blockId = document.CreateBlockDefinition("Protected", default);
        document.GetBlock(blockId).SetReadOnly(true);
        var line = session.CadEditor.AddLine(default, new(10, 0));
        session.SelectEntities([line]);
        var executor = new CadWorkspaceToolExecutor(workspace);

        await Execute(executor, "edit_block", new { block = "Protected" }, success: false);

        Assert.False(session.IsEditingBlock);
        Assert.Equal(Direct2dCad.Db.BlockId.ModelSpace, session.CadEditor.ActiveOwnerBlockId);
        Assert.Contains(line, session.CadEditor.Selection.EntityIds);
    }

    [Fact]
    public async Task AutomaticSelectionDoesNotAddEditorHistoryButExplicitSelectionDoes()
    {
        using var workspace = new HeadlessWorkspace();
        var session = workspace.CreateDocument("Selection history").Session;
        var line = session.CadEditor.AddLine(default, new(10, 0));
        var executor = new CadWorkspaceToolExecutor(workspace);

        await Execute(executor, "move_entities", new { entity_ids = new[] { line.Value }, delta_x = 5, delta_y = 0 });

        Assert.Equal("Select", session.ToolMode);
        Assert.Contains(line, session.CadEditor.Selection.EntityIds);
        Assert.False(session.CadEditor.EditorCommands.CanUndo);
        Assert.True(session.CadEditor.DocumentCommands.CanUndo);

        session.SelectEntities([]);
        await Execute(executor, "select_entities", new { entity_ids = new[] { line.Value } });
        Assert.True(session.CadEditor.EditorCommands.CanUndo);
        session.CadEditor.EditorCommands.Undo();
        Assert.Empty(session.CadEditor.Selection.EntityIds);
    }

    [Fact]
    public void AutomaticSelectionFiltersMissingEntitiesAndOtherEditingSpaces()
    {
        using var session = new CadToolDocumentSession(CadDocument.Create("Selection filter"));
        var line = session.CadEditor.AddLine(default, new(10, 0));
        session.SelectEntities([line, line, new(long.MaxValue)]);
        Assert.Equal(new[] { line }, session.CadEditor.Selection.EntityIds);

        var block = session.CadEditor.Document.CreateBlockDefinition("Block", default);
        session.EditBlockDefinition(block);
        session.SelectEntities([line]);
        Assert.Empty(session.CadEditor.Selection.EntityIds);
        Assert.False(session.CadEditor.EditorCommands.CanUndo);
    }

    [Fact]
    public void ApplicationAssemblyAndPublicContractsDoNotDependOnUiOrNativeBackend()
    {
        var assembly = typeof(CadWorkspaceToolExecutor).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a =>
            a.Name!.Contains("ViewModels") || a.Name.Contains("Direct2D") ||
            a.Name.Contains("AvalonDock") || a.Name.StartsWith("Vortice") || a.Name == "PresentationFramework");
        Assert.Equal(typeof(ICadWorkspaceDocument), typeof(CadToolWorkspaceDocument).GetProperty("Host")!.PropertyType);
        Assert.Equal(typeof(ICadToolDocumentSession), typeof(CadToolWorkspaceDocument).GetProperty("Session")!.PropertyType);
    }

    private static async Task<JsonElement> Execute(CadWorkspaceToolExecutor executor, string tool, object args, bool success = true)
    {
        var result = await executor.ExecuteAsync(new(Guid.NewGuid().ToString(), tool, JsonSerializer.Serialize(args)), default);
        using var json = JsonDocument.Parse(result);
        Assert.Equal(success, json.RootElement.GetProperty("success").GetBoolean());
        return json.RootElement.Clone();
    }

    private sealed class HeadlessWorkspace : ICadToolWorkspace, IDisposable
    {
        public Func<string, int, CancellationToken, Task<CadToolImage>>? CaptureHandler { get; set; }
        public Func<string, CancellationToken, Task<bool>>? PrintHandler { get; set; }
        public Task<CadToolImage> CaptureViewAsync(string id, int size, CancellationToken token) =>
            CaptureHandler?.Invoke(id, size, token) ?? throw new NotSupportedException("View capture is unavailable in this host.");
        public Task<bool> PrintDocumentAsync(string id, CancellationToken token) =>
            PrintHandler?.Invoke(id, token) ?? throw new NotSupportedException("Printing is unavailable in this host.");
        private readonly List<CadToolWorkspaceDocument> _documents = [];
        private string? _active;
        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => _documents.Select(d => d with { IsActive = d.DocumentId == _active }).ToArray();
        public CadToolWorkspaceDocument? GetActiveDocument() => GetDocuments().FirstOrDefault(d => d.IsActive);
        public CadToolWorkspaceDocument GetRequiredDocument(string id) => GetDocuments().Single(d => d.DocumentId == id);
        public CadToolWorkspaceDocument CreateDocument(string? name)
        {
            var host = new HeadlessDocument(CadDocument.Create(name ?? "Untitled"));
            _active = Guid.NewGuid().ToString();
            var result = new CadToolWorkspaceDocument(_active, host.Session.CadEditor.Document.Id.Value,
                name ?? "Untitled", "", false, true, host);
            _documents.Add(result);
            return result;
        }
        public bool ActivateDocument(string id) { _active = GetRequiredDocument(id).DocumentId; return true; }
        public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public bool RenameDocument(string id, string name) => throw new NotSupportedException();
        public Task<bool> SaveDocumentAsync(string id, string? path, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> CloseDocumentAsync(string id) => throw new NotSupportedException();
        public void Dispose() { foreach (var document in _documents) ((HeadlessDocument)document.Host).Dispose(); }
    }

    private sealed class HeadlessDocument(CadDocument document) : ICadWorkspaceDocument, IDisposable
    {
        private CadToolDocumentSession _session = new(document);
        public ICadToolDocumentSession Session => _session;
        public void Load(CadDocument value, string filePath) { _session.Dispose(); _session = new(value); }
        public Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default) => operation(token);
        public void Dispose() => _session.Dispose();
    }
}
