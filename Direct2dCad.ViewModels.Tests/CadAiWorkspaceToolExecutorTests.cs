using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.AI;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAiWorkspaceToolExecutorTests
{
    [Fact]
    public void ToolDefinitions_AreUniqueAndContainWorkspaceAndAppearanceTools()
    {
        var tools = CadAiWorkspaceToolExecutor.ToolDefinitions;

        Assert.Equal(tools.Count, tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(tools, tool => tool.Name == "list_documents");
        Assert.Contains(tools, tool => tool.Name == "create_document");
        Assert.Contains(tools, tool => tool.Name == "close_document");
        Assert.Contains(tools, tool => tool.Name == "list_document_catalog");
        Assert.Contains(tools, tool => tool.Name == "set_entity_common_properties");
        Assert.Contains(tools, tool => tool.Name == "set_entity_fill");
        Assert.Contains(tools, tool => tool.Name == "set_entity_stroke_style");
    }

    [Fact]
    public void LegacyDocumentTools_AllAcceptOptionalDocumentId()
    {
        var workspaceTools = CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);

        foreach (var legacyTool in CadAiToolExecutor.ToolDefinitions)
        {
            var properties = workspaceTools[legacyTool.Name].Parameters.GetProperty("properties");
            Assert.True(properties.TryGetProperty("document_id", out var documentId), legacyTool.Name);
            Assert.Equal(JsonValueKind.String, documentId.GetProperty("type").ValueKind);
        }
    }

    [Fact]
    public void CreationTools_ExposeSupportedAppearanceSchemas()
    {
        var tools = CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var circle = tools["add_circle"].Parameters.GetProperty("properties");
        var text = tools["add_text"].Parameters.GetProperty("properties");

        Assert.True(circle.TryGetProperty("color", out _));
        Assert.True(circle.TryGetProperty("line_weight", out _));
        Assert.True(circle.TryGetProperty("stroke_style", out _));
        Assert.True(circle.TryGetProperty("fill", out _));
        Assert.True(text.TryGetProperty("color", out _));
        Assert.False(text.TryGetProperty("fill", out _));
    }

    [Theory]
    [InlineData("#123456", 255, 0x12, 0x34, 0x56)]
    [InlineData("80123456", 0x80, 0x12, 0x34, 0x56)]
    [InlineData("red", 255, 255, 0, 0)]
    public void ParseColor_ParsesProtocolFormats(
        string value,
        byte expectedA,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        var color = CadAiWorkspaceToolExecutor.ParseColor(value);

        Assert.Equal(new CadColor(expectedA, expectedR, expectedG, expectedB), color);
    }

    [Fact]
    public void ParseColor_RejectsUnknownValues()
    {
        Assert.Throws<ArgumentException>(() => CadAiWorkspaceToolExecutor.ParseColor("not-a-color"));
    }

    [Fact]
    public async Task WorkspaceTools_CanCreateDocumentWithoutAnActiveDocument()
    {
        var workspace = new FakeWorkspaceService();
        var executor = new CadAiWorkspaceToolExecutor(workspace);

        var result = await executor.ExecuteAsync(
            new AiToolCall("call-1", "create_document", "{\"name\":\"AI Drawing\"}"),
            CancellationToken.None);
        var listResult = await executor.ExecuteAsync(
            new AiToolCall("call-2", "list_documents", "{}"),
            CancellationToken.None);

        Assert.True(ReadSuccess(result));
        using var listJson = JsonDocument.Parse(listResult);
        Assert.Equal("document-1", listJson.RootElement.GetProperty("result").GetProperty("default_document_id").GetString());
    }

    [Fact]
    public async Task ActivateDocument_ChangesRequestDefaultDocument()
    {
        var workspace = new FakeWorkspaceService();
        workspace.Add("document-1", "One", isActive: true);
        workspace.Add("document-2", "Two", isActive: false);
        var executor = new CadAiWorkspaceToolExecutor(workspace);

        var result = await executor.ExecuteAsync(
            new AiToolCall("call-1", "activate_document", "{\"document_id\":\"document-2\"}"),
            CancellationToken.None);
        var listResult = await executor.ExecuteAsync(
            new AiToolCall("call-2", "list_documents", "{}"),
            CancellationToken.None);

        Assert.True(ReadSuccess(result));
        using var listJson = JsonDocument.Parse(listResult);
        Assert.Equal("document-2", listJson.RootElement.GetProperty("result").GetProperty("default_document_id").GetString());
    }

    private static bool ReadSuccess(string result)
    {
        using var json = JsonDocument.Parse(result);
        return json.RootElement.GetProperty("success").GetBoolean();
    }

    private sealed class FakeWorkspaceService : ICadAiWorkspaceService
    {
        private readonly List<CadAiWorkspaceDocument> _documents = [];
        private string? _activeDocumentId;

        public void Add(string documentId, string name, bool isActive)
        {
            if (isActive)
                _activeDocumentId = documentId;
            _documents.Add(CreateDescriptor(documentId, name));
        }

        public IReadOnlyList<CadAiWorkspaceDocument> GetDocuments() =>
            _documents.Select(document => document with
            {
                IsActive = document.DocumentId == _activeDocumentId
            }).ToArray();

        public CadAiWorkspaceDocument? GetActiveDocument() =>
            _activeDocumentId is null ? null : GetRequiredDocument(_activeDocumentId);

        public CadAiWorkspaceDocument GetRequiredDocument(string documentId)
        {
            var document = _documents.FirstOrDefault(candidate => candidate.DocumentId == documentId)
                ?? throw new ArgumentException($"Open document not found: {documentId}");
            return document with { IsActive = document.DocumentId == _activeDocumentId };
        }

        public CadAiWorkspaceDocument CreateDocument(string? name)
        {
            var id = $"document-{_documents.Count + 1}";
            Add(id, string.IsNullOrWhiteSpace(name) ? "Untitled" : name, isActive: true);
            return GetRequiredDocument(id);
        }

        public Task<CadAiWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool ActivateDocument(string documentId)
        {
            _ = GetRequiredDocument(documentId);
            _activeDocumentId = documentId;
            return true;
        }

        public bool RenameDocument(string documentId, string name) => throw new NotSupportedException();

        public Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CloseDocumentAsync(string documentId) => throw new NotSupportedException();

        private CadAiWorkspaceDocument CreateDescriptor(string documentId, string name) => new(
            documentId,
            _documents.Count + 1,
            name,
            string.Empty,
            IsModified: false,
            IsActive: documentId == _activeDocumentId,
            EditorTab: null!);
    }
}
