using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

internal sealed class ToolExecutionWorkspace : ICadToolWorkspace, IDisposable
{
    private readonly List<(CadToolboxTestContext Context, EditorTabViewModel Tab)> _documents = [];
    private string? _active;
    public bool AllowClose { get; set; } = true;
    public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => _documents.Select(item => Describe(item.Tab)).ToArray();
    public CadToolWorkspaceDocument? GetActiveDocument() => GetDocuments().FirstOrDefault(item => item.DocumentId == _active);
    public CadToolWorkspaceDocument GetRequiredDocument(string documentId) => GetDocuments().Single(item => item.DocumentId == documentId);
    public CadToolWorkspaceDocument CreateDocument(string? name)
    {
        var context = new CadToolboxTestContext();
        var tab = context.CreateEditorTab(new RecordingDialogService(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        if (name is not null) tab.TryRenameDocument(name);
        _documents.Add((context, tab));
        _active = tab.ContentId;
        return Describe(tab);
    }
    public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDocument(Path.GetFileNameWithoutExtension(filePath)));
    }
    public bool ActivateDocument(string documentId)
    {
        _active = GetRequiredDocument(documentId).DocumentId;
        return true;
    }
    public bool RenameDocument(string documentId, string name) => GetRequiredDocument(documentId).EditorTab.TryRenameDocument(name);
    public Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken) =>
        GetRequiredDocument(documentId).EditorTab.SaveToFileForWorkspaceToolAsync(filePath ?? "test.d2cad", cancellationToken);
    public Task<bool> CloseDocumentAsync(string documentId)
    {
        if (!AllowClose) return Task.FromResult(false);
        var item = _documents.Single(item => item.Tab.ContentId == documentId);
        _documents.Remove(item);
        item.Tab.Dispose();
        item.Context.Dispose();
        if (_active == documentId) _active = _documents.FirstOrDefault().Tab?.ContentId;
        return Task.FromResult(true);
    }
    public void Dispose()
    {
        foreach (var item in _documents)
        {
            item.Tab.Dispose();
            item.Context.Dispose();
        }
        _documents.Clear();
    }
    private CadToolWorkspaceDocument Describe(EditorTabViewModel tab) => new(tab.ContentId,
        tab.CadDocumentViewModel.CadEditor.Document.Id.Value, tab.DocumentName, tab.CurrentFilePath, tab.IsModified,
        tab.ContentId == _active, tab);

    public static async Task<JsonElement> Execute(CadWorkspaceToolExecutor executor, string tool, object arguments, bool success = true)
    {
        var json = arguments is string text ? text : JsonSerializer.Serialize(arguments);
        var result = await executor.ExecuteAsync(new AiToolCall(Guid.NewGuid().ToString(), tool, json), default);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean() == success, $"{tool}: {result}");
        return root.Clone();
    }
}
