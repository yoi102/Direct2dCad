using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
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
        Assert.Contains(tools, tool => tool.Name == "get_entity_properties");
        Assert.Contains(tools, tool => tool.Name == "set_text_properties");
        Assert.Contains(tools, tool => tool.Name == "set_block_reference_definition");
        Assert.Contains(tools, tool => tool.Name == "set_block_definition_base_point");
        Assert.Contains(tools, tool => tool.Name == "replace_embedded_entity_data");
        Assert.Contains(tools, tool => tool.Name == "get_entity_geometry");
        Assert.Contains(tools, tool => tool.Name == "set_entity_geometry");
        Assert.Contains(tools, tool => tool.Name == "transform_entities");
        Assert.Contains(tools, tool => tool.Name == "duplicate_entities");
        Assert.Contains(tools, tool => tool.Name == "add_entities");
        Assert.Contains(tools, tool => tool.Name == "list_layers");
        Assert.Contains(tools, tool => tool.Name == "create_layer");
        Assert.Contains(tools, tool => tool.Name == "rename_layer");
        Assert.Contains(tools, tool => tool.Name == "delete_layer");
        Assert.Contains(tools, tool => tool.Name == "set_layer_properties");
        Assert.Contains(tools, tool => tool.Name == "reorder_layers");
        Assert.Contains(tools, tool => tool.Name == "create_block");
        Assert.Contains(tools, tool => tool.Name == "insert_block");
        Assert.Contains(tools, tool => tool.Name == "add_composite_path");
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
    [InlineData("add_arc", false)]
    [InlineData("add_ellipse", true)]
    [InlineData("add_polygon", true)]
    [InlineData("add_spline", true)]
    public void NewCreationTools_ExposeDocumentAppearanceAndExpectedFill(
        string toolName,
        bool supportsFill)
    {
        var tool = CadAiWorkspaceToolExecutor.ToolDefinitions.Single(candidate => candidate.Name == toolName);
        var properties = tool.Parameters.GetProperty("properties");

        Assert.True(properties.TryGetProperty("document_id", out _));
        Assert.True(properties.TryGetProperty("color", out _));
        Assert.True(properties.TryGetProperty("stroke_style", out _));
        Assert.Equal(supportsFill, properties.TryGetProperty("fill", out _));
    }

    [Fact]
    public void GeometryTools_ExposeTypedReadWriteAndTransformParameters()
    {
        var tools = CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var setGeometry = tools["set_entity_geometry"].Parameters.GetProperty("properties");
        var transform = tools["transform_entities"].Parameters.GetProperty("properties");

        Assert.True(setGeometry.TryGetProperty("entity_id", out _));
        Assert.True(setGeometry.TryGetProperty("fit_points", out _));
        Assert.True(setGeometry.TryGetProperty("start_angle_degrees", out _));
        Assert.True(setGeometry.TryGetProperty("radius_x", out _));
        Assert.True(transform.TryGetProperty("operation", out _));
        Assert.True(transform.TryGetProperty("axis_angle_degrees", out _));
        Assert.True(transform.TryGetProperty("factor", out _));

        var common = tools["set_entity_common_properties"].Parameters.GetProperty("properties");
        Assert.True(common.TryGetProperty("locked", out _));
        Assert.True(common.TryGetProperty("opacity", out _));
    }

    [Fact]
    public void P1Tools_ExposeDocumentScopedLayerBlockAndCompositePathSchemas()
    {
        var tools = CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        foreach (var name in new[] { "create_layer", "create_block", "insert_block", "add_composite_path" })
        {
            Assert.True(tools[name].Parameters.GetProperty("properties").TryGetProperty("document_id", out _), name);
        }

        var composite = tools["add_composite_path"].Parameters.GetProperty("properties");
        Assert.True(composite.TryGetProperty("segments", out var segments));
        Assert.Equal(1, segments.GetProperty("minItems").GetInt32());
        Assert.True(composite.TryGetProperty("fill", out _));
    }

    [Fact]
    public void LayerTools_ExposeStateOrderAndDeleteConfirmationSchemas()
    {
        var tools = CadAiWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var setProperties = tools["set_layer_properties"].Parameters.GetProperty("properties");
        var delete = tools["delete_layer"].Parameters.GetProperty("properties");
        var reorder = tools["reorder_layers"].Parameters.GetProperty("properties");

        Assert.True(setProperties.TryGetProperty("visible", out _));
        Assert.True(setProperties.TryGetProperty("locked", out _));
        Assert.True(setProperties.TryGetProperty("frozen", out _));
        Assert.True(setProperties.TryGetProperty("drawing_priority", out _));
        Assert.True(delete.GetProperty("delete_entities").GetProperty("const").GetBoolean());
        Assert.True(reorder.GetProperty("layer_order").GetProperty("uniqueItems").GetBoolean());
    }

    [Fact]
    public void LayerTools_MutationsShareOneUndoBatch()
    {
        var document = CadDocument.Create("Test");
        var editor = new CadEditor(document);
        var batchId = Guid.NewGuid();
        var tools = new CadAiLayerTools(
            document,
            command => editor.ExecuteInBatch(command, batchId));

        ExecuteLayerTool(tools, "create_layer", """
            { "name": "Mechanical", "color": "#123456", "line_weight": 0.35 }
            """);
        ExecuteLayerTool(tools, "rename_layer", """
            { "layer": "Mechanical", "new_name": "Housing" }
            """);
        ExecuteLayerTool(tools, "set_layer_properties", """
            { "layer": "Housing", "visible": false, "locked": true, "frozen": true, "drawing_priority": 12 }
            """);
        ExecuteLayerTool(tools, "reorder_layers", """
            { "layer_order": ["Housing", "DefaultLayer"] }
            """);

        var layer = Assert.Single(document.Layers.Values, candidate => candidate.Name == "Housing");
        Assert.False(layer.IsVisible);
        Assert.True(layer.IsLocked);
        Assert.True(layer.IsFrozen);
        Assert.True(document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id) >
                    document.DocumentSettings.LayerDrawingPriority.GetPriority(LayerId.Default));

        editor.UndoBatch(batchId);

        Assert.Single(document.Layers);
        Assert.True(document.Layers.ContainsKey(LayerId.Default));
    }

    [Fact]
    public void DeleteLayer_RequiresConfirmationAndUndoRestoresEntities()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Disposable", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), layerId);
        var editor = new CadEditor(document);
        var batchId = Guid.NewGuid();
        var tools = new CadAiLayerTools(
            document,
            command => editor.ExecuteInBatch(command, batchId));

        Assert.Throws<ArgumentException>(() => ExecuteLayerTool(
            tools,
            "delete_layer",
            """{ "layer": "Disposable", "delete_entities": false }"""));

        ExecuteLayerTool(
            tools,
            "delete_layer",
            """{ "layer": "Disposable", "delete_entities": true }""");

        Assert.False(document.TryGetLayer(layerId, out _));
        Assert.True(line.IsErased);

        editor.UndoBatch(batchId);

        Assert.True(document.TryGetLayer(layerId, out _));
        Assert.False(line.IsErased);
    }

    [Fact]
    public void CompositePathParser_CreatesContinuousMixedSegments()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "start": { "x": 0, "y": 0 },
              "segments": [
                { "type": "line", "end": { "x": 10, "y": 0 } },
                { "type": "arc", "center": { "x": 10, "y": 5 }, "sweep_angle_degrees": 90 },
                {
                  "type": "cubic_bezier",
                  "control1": { "x": 12, "y": 12 },
                  "control2": { "x": 18, "y": 12 },
                  "end": { "x": 20, "y": 8 }
                },
                { "type": "spline", "fit_points": [{ "x": 20, "y": 8 }, { "x": 25, "y": 0 }] }
              ],
              "closed": true
            }
            """);

        var geometry = CadAiCompositePathTools.Parse(arguments.RootElement);

        Assert.True(geometry.Closed);
        Assert.Collection(
            geometry.Segments,
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeLineSegment>(segment),
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeArcSegment>(segment),
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeBezierSegment>(segment),
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeSplineSegment>(segment));
    }

    [Fact]
    public void BulkCreationParser_AcceptsAllSupportedEntityTypes()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "entities": [
                { "type": "line" },
                { "type": "circle" },
                { "type": "rectangle" },
                { "type": "text" },
                { "type": "polyline" },
                { "type": "arc" },
                { "type": "ellipse" },
                { "type": "polygon" },
                { "type": "spline" }
                ,{ "type": "composite_path" }
              ]
            }
            """);

        var items = CadAiBulkCreationTools.Parse(arguments.RootElement);

        Assert.Equal(10, items.Count);
        Assert.Equal("add_line", items[0].ToolName);
        Assert.Equal("add_composite_path", items[^1].ToolName);
    }

    [Fact]
    public void BulkCreationParser_RejectsUnsupportedTypeBeforeExecution()
    {
        using var arguments = JsonDocument.Parse("""{ "entities": [{ "type": "cat" }] }""");

        var exception = Assert.Throws<ArgumentException>(() => CadAiBulkCreationTools.Parse(arguments.RootElement));

        Assert.Contains("Unsupported bulk entity type", exception.Message);
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
    public async Task ActivateDocument_DoesNotChangeRequestStartTargetDocument()
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
        Assert.Equal("document-1", listJson.RootElement.GetProperty("result").GetProperty("request_target_document_id").GetString());
        Assert.Equal("document-1", listJson.RootElement.GetProperty("result").GetProperty("default_document_id").GetString());
    }

    [Fact]
    public async Task DocumentScopedTool_WithoutRequestTarget_DoesNotCreateDocument()
    {
        var workspace = new FakeWorkspaceService();
        var executor = new CadAiWorkspaceToolExecutor(workspace);

        var result = await executor.ExecuteAsync(
            new AiToolCall("call-1", "add_line", """{"x1":0,"y1":0,"x2":10,"y2":10}"""),
            CancellationToken.None);

        Assert.False(ReadSuccess(result));
        Assert.Empty(workspace.GetDocuments());
    }

    [Fact]
    public void ToolSchema_ExposesEditorStateAndCubicBezierSegments()
    {
        var stateTool = Assert.Single(
            CadAiWorkspaceToolExecutor.ToolDefinitions,
            tool => tool.Name == "get_editor_state");
        var bulkTool = Assert.Single(
            CadAiWorkspaceToolExecutor.ToolDefinitions,
            tool => tool.Name == "add_entities");

        Assert.True(stateTool.Parameters.GetProperty("properties").TryGetProperty("document_id", out _));
        Assert.Contains("cubic_bezier", bulkTool.Parameters.GetRawText(), StringComparison.Ordinal);
    }

    private static bool ReadSuccess(string result)
    {
        using var json = JsonDocument.Parse(result);
        return json.RootElement.GetProperty("success").GetBoolean();
    }

    private static object ExecuteLayerTool(CadAiLayerTools tools, string name, string arguments)
    {
        using var json = JsonDocument.Parse(arguments);
        return tools.Execute(name, json.RootElement);
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
