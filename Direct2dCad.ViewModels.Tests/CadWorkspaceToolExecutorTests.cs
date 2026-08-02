using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadWorkspaceToolExecutorTests
{
    [Fact]
    public void ToolDefinitions_AreUniqueAndContainWorkspaceAndAppearanceTools()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions;

        Assert.Equal(tools.Count, tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(tools, tool => tool.Name == "list_documents");
        Assert.Contains(tools, tool => tool.Name == "create_document");
        Assert.Contains(tools, tool => tool.Name == "close_document");
        Assert.Contains(tools, tool => tool.Name == "list_document_catalog");
        Assert.Contains(tools, tool => tool.Name == "set_entity_common_properties");
        Assert.Contains(tools, tool => tool.Name == "set_entity_fill");
        Assert.Contains(tools, tool => tool.Name == "set_entity_stroke_style");
        Assert.Contains(tools, tool => tool.Name == "set_entity_specific_properties");
        Assert.Contains(tools, tool => tool.Name == "set_text_style_properties");
        Assert.Contains(tools, tool => tool.Name == "set_graphic_style_properties");
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
        Assert.Contains(tools, tool => tool.Name == "list_blocks");
        Assert.Contains(tools, tool => tool.Name == "rename_block");
        Assert.Contains(tools, tool => tool.Name == "delete_block");
        Assert.Contains(tools, tool => tool.Name == "edit_block");
        Assert.Contains(tools, tool => tool.Name == "exit_block_edit");
        Assert.Contains(tools, tool => tool.Name == "create_line_type");
        Assert.Contains(tools, tool => tool.Name == "rename_line_type");
        Assert.Contains(tools, tool => tool.Name == "delete_line_type");
        Assert.Contains(tools, tool => tool.Name == "create_graphic_style");
        Assert.Contains(tools, tool => tool.Name == "create_fill_style");
        Assert.Contains(tools, tool => tool.Name == "create_hatch_pattern");
        Assert.Contains(tools, tool => tool.Name == "add_composite_path");
        Assert.Contains(tools, tool => tool.Name == "add_shape_text");
        Assert.Contains(tools, tool => tool.Name == "add_ellipse_arc");
        Assert.Contains(tools, tool => tool.Name == "insert_image_from_file");
        Assert.Contains(tools, tool => tool.Name == "add_ole_object");
        Assert.Contains(tools, tool => tool.Name == "set_ole_object_data");
        Assert.Contains(tools, tool => tool.Name == "get_agent_capabilities");
    }

    [Fact]
    public void LegacyDocumentTools_AllAcceptOptionalDocumentId()
    {
        var workspaceTools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);

        foreach (var legacyTool in CadDocumentToolExecutor.ToolDefinitions)
        {
            var properties = workspaceTools[legacyTool.Name].Parameters.GetProperty("properties");
            Assert.True(properties.TryGetProperty("document_id", out var documentId), legacyTool.Name);
            Assert.Equal(JsonValueKind.String, documentId.GetProperty("type").ValueKind);
        }
    }

    [Fact]
    public void CreationTools_ExposeSupportedAppearanceSchemas()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var circle = tools["add_circle"].Parameters.GetProperty("properties");
        var rectangle = tools["add_rectangle"].Parameters.GetProperty("properties");
        var text = tools["add_text"].Parameters.GetProperty("properties");

        Assert.True(circle.TryGetProperty("color", out _));
        Assert.True(circle.TryGetProperty("line_weight", out _));
        Assert.True(circle.TryGetProperty("stroke_style", out _));
        Assert.True(circle.TryGetProperty("fill", out _));
        Assert.True(circle.TryGetProperty("locked", out _));
        Assert.True(rectangle.TryGetProperty("corner_radius_x", out _));
        Assert.True(rectangle.TryGetProperty("corner_radius_y", out _));
        Assert.True(text.TryGetProperty("color", out _));
        Assert.False(text.TryGetProperty("fill", out _));
        Assert.False(text.TryGetProperty("stroke_style", out _));
        Assert.True(text.TryGetProperty("font_family", out _));
        Assert.True(text.TryGetProperty("inverted", out _));
    }

    [Theory]
    [InlineData("add_arc", false)]
    [InlineData("add_ellipse", true)]
    [InlineData("add_ellipse_arc", false)]
    [InlineData("add_polygon", true)]
    [InlineData("add_spline", true)]
    public void NewCreationTools_ExposeDocumentAppearanceAndExpectedFill(
        string toolName,
        bool supportsFill)
    {
        var tool = CadWorkspaceToolExecutor.ToolDefinitions.Single(candidate => candidate.Name == toolName);
        var properties = tool.Parameters.GetProperty("properties");

        Assert.True(properties.TryGetProperty("document_id", out _));
        Assert.True(properties.TryGetProperty("color", out _));
        Assert.True(properties.TryGetProperty("stroke_style", out _));
        Assert.Equal(supportsFill, properties.TryGetProperty("fill", out _));
    }

    [Fact]
    public void GeometryTools_ExposeTypedReadWriteAndTransformParameters()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var setGeometry = tools["set_entity_geometry"].Parameters.GetProperty("properties");
        var transform = tools["transform_entities"].Parameters.GetProperty("properties");

        Assert.True(setGeometry.TryGetProperty("entity_id", out _));
        Assert.True(setGeometry.TryGetProperty("fit_points", out _));
        Assert.True(setGeometry.TryGetProperty("start_angle_degrees", out _));
        Assert.True(transform.TryGetProperty("operation", out _));
        Assert.True(transform.TryGetProperty("axis_angle_degrees", out _));
        Assert.True(transform.TryGetProperty("factor", out _));
        Assert.True(setGeometry.TryGetProperty("entity_type", out _));
        var geometrySchema = tools["set_entity_geometry"].Parameters;
        Assert.True(geometrySchema.TryGetProperty("allOf", out var geometryRules));
        Assert.NotEmpty(geometryRules.EnumerateArray());
        var transformSchema = tools["transform_entities"].Parameters;
        Assert.True(transformSchema.TryGetProperty("allOf", out var transformRules));
        Assert.Equal(4, transformRules.GetArrayLength());
    }

    [Fact]
    public void DestructiveAndAppearanceSchemasExposeExplicitSafetyRules()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var delete = tools["delete_entities"].Parameters;
        Assert.True(delete.GetProperty("properties").GetProperty("confirm").GetProperty("const").GetBoolean());
        Assert.Contains("confirm", delete.GetProperty("required").EnumerateArray().Select(value => value.GetString()));

        var common = tools["set_entity_common_properties"].Parameters;
        Assert.True(common.TryGetProperty("not", out var exclusive));
        Assert.Equal(2, exclusive.GetProperty("required").GetArrayLength());

        var fill = tools["set_entity_fill"].Parameters;
        Assert.True(fill.TryGetProperty("allOf", out var fillRules));
        Assert.NotEmpty(fillRules.EnumerateArray());
        var fillProperties = fill.GetProperty("properties");
        Assert.Contains("gradient", fillProperties.GetProperty("mode").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.True(fillProperties.TryGetProperty("stops", out _));
    }

    [Fact]
    public void AgentContractToolExposesRulesAndOptionalExamples()
    {
        var tool = CadWorkspaceToolExecutor.ToolDefinitions.Single(candidate =>
            candidate.Name == "get_agent_capabilities");
        var properties = tool.Parameters.GetProperty("properties");

        Assert.True(properties.GetProperty("include_examples").GetProperty("type").GetString() == "boolean");
        Assert.Contains("Agent Contract", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpecificPropertyTool_ExposesAllTypeSpecificMutationGroups()
    {
        var tool = CadWorkspaceToolExecutor.ToolDefinitions.Single(candidate =>
            candidate.Name == "set_entity_specific_properties");
        var properties = tool.Parameters.GetProperty("properties");

        foreach (var name in new[]
                 {
                     "text", "text_style", "font_family", "shape_font", "inverted",
                     "inverted_margin_factor", "opacity", "block_definition"
                 })
        {
            Assert.True(properties.TryGetProperty(name, out _), name);
        }
    }

    [Fact]
    public void SharedStyleTools_ExposeAllEditableStyleProperties()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var textProperties = tools["set_text_style_properties"].Parameters.GetProperty("properties");
        var graphicProperties = tools["set_graphic_style_properties"].Parameters.GetProperty("properties");

        foreach (var name in new[] { "font_family", "text_height", "width_factor", "oblique_angle_degrees", "bold", "italic" })
            Assert.True(textProperties.TryGetProperty(name, out _), name);
        foreach (var name in new[] { "color", "line_weight", "line_type_id" })
            Assert.True(graphicProperties.TryGetProperty(name, out _), name);
        Assert.Equal(1, graphicProperties.GetProperty("line_type_id").GetProperty("minimum").GetInt64());
    }

    [Fact]
    public void CreationSpecificPropertyValidation_RejectsPropertiesOnWrongTypes()
    {
        var document = CadDocument.Create("Test");
        using var valid = JsonDocument.Parse("""
            { "shape_font": "unicode", "inverted": true, "inverted_margin_factor": 0.2 }
            """);
        using var invalid = JsonDocument.Parse("""{ "shape_font": "unicode" }""");

        CadEntitySpecificPropertyTools.ValidateCreationArguments(
            document,
            "add_shape_text",
            valid.RootElement);
        Assert.Throws<NotSupportedException>(() =>
            CadEntitySpecificPropertyTools.ValidateCreationArguments(
                document,
                "add_text",
                invalid.RootElement));
    }

    [Fact]
    public void P1Tools_ExposeDocumentScopedLayerBlockAndCompositePathSchemas()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        foreach (var name in new[] { "create_layer", "create_block", "insert_block", "add_composite_path" })
        {
            Assert.True(tools[name].Parameters.GetProperty("properties").TryGetProperty("document_id", out _), name);
        }

        var composite = tools["add_composite_path"].Parameters.GetProperty("properties");
        Assert.True(composite.TryGetProperty("segments", out var segments));
        Assert.Equal(1, segments.GetProperty("minItems").GetInt32());
        Assert.True(composite.TryGetProperty("fill", out _));
        Assert.Contains("gradient", composite.GetProperty("fill").GetProperty("properties")
            .GetProperty("mode").GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString()));
    }

    [Fact]
    public void EmbeddedObjectTools_ExposePersistedDataAndAppearanceState()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var addOle = tools["add_ole_object"].Parameters.GetProperty("properties");
        var setOle = tools["set_ole_object_data"].Parameters.GetProperty("properties");
        var image = tools["insert_image_from_file"].Parameters.GetProperty("properties");

        Assert.True(addOle.TryGetProperty("ole_base64", out var oleBytes));
        Assert.Equal("base64", oleBytes.GetProperty("contentEncoding").GetString());
        Assert.True(addOle.TryGetProperty("opacity", out _));
        Assert.True(addOle.TryGetProperty("locked", out _));
        Assert.True(setOle.TryGetProperty("entity_id", out _));
        Assert.True(setOle.TryGetProperty("ole_base64", out _));
        Assert.True(image.TryGetProperty("locked", out _));
    }

    [Fact]
    public void AgentContract_DescribesConditionalCapabilitiesAndLineTypeLimit()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var contractTool = tools["get_agent_capabilities"];
        Assert.Contains("Contract", contractTool.Description, StringComparison.OrdinalIgnoreCase);

        var capabilities = CadAgentContract.CreateCapabilities([], null, includeExamples: false);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(capabilities));
        var root = json.RootElement;
        Assert.Equal("1.3", root.GetProperty("contract_version").GetString());
        Assert.Contains("line_types", root.GetProperty("rules").EnumerateObject().Select(property => property.Name));
        var circle = root.GetProperty("entity_capabilities").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "Circle");
        Assert.True(circle.TryGetProperty("conditions", out _));
    }

    [Fact]
    public void LayerTools_ExposeStateOrderAndDeleteConfirmationSchemas()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
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
        var tools = new CadLayerTools(
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
        var tools = new CadLayerTools(
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
                { "type": "spline", "fit_points": [{ "x": 20, "y": 8 }, { "x": 25, "y": 0 }] }
              ],
              "closed": true
            }
            """);

        var geometry = CadCompositePathTools.Parse(arguments.RootElement);

        Assert.True(geometry.Closed);
        Assert.Collection(
            geometry.Segments,
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeLineSegment>(segment),
            segment => Assert.IsType<Direct2dCad.Db.Data.Entities.CadCompositeArcSegment>(segment),
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
                { "type": "shape_text" },
                { "type": "polyline" },
                { "type": "arc" },
                { "type": "ellipse" },
                { "type": "ellipse_arc" },
                { "type": "polygon" },
                { "type": "spline" }
                ,{ "type": "composite_path" }
              ]
            }
            """);

        var items = CadBulkCreationTools.Parse(arguments.RootElement);

        Assert.Equal(12, items.Count);
        Assert.Equal("add_line", items[0].ToolName);
        Assert.Equal("add_composite_path", items[^1].ToolName);
    }

    [Fact]
    public void BulkCreationSchema_DeclaresPerTypeRequiredGeometry()
    {
        var tool = CadWorkspaceToolExecutor.ToolDefinitions.Single(candidate => candidate.Name == "add_entities");
        var item = tool.Parameters
            .GetProperty("properties")
            .GetProperty("entities")
            .GetProperty("items");
        var rules = item.GetProperty("allOf").EnumerateArray().ToArray();

        Assert.Equal(12, rules.Length);
        var ellipseArc = rules.Single(rule =>
            rule.GetProperty("if")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString() == "ellipse_arc");
        var required = ellipseArc.GetProperty("then").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("radius_x", required);
        Assert.Contains("sweep_angle_degrees", required);
    }

    [Fact]
    public void BulkCreationParser_RejectsUnsupportedTypeBeforeExecution()
    {
        using var arguments = JsonDocument.Parse("""{ "entities": [{ "type": "cat" }] }""");

        var exception = Assert.Throws<ArgumentException>(() => CadBulkCreationTools.Parse(arguments.RootElement));

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
        var color = CadWorkspaceToolExecutor.ParseColor(value);

        Assert.Equal(new CadColor(expectedA, expectedR, expectedG, expectedB), color);
    }

    [Fact]
    public void ParseColor_RejectsUnknownValues()
    {
        Assert.Throws<ArgumentException>(() => CadWorkspaceToolExecutor.ParseColor("not-a-color"));
    }

    [Fact]
    public async Task WorkspaceTools_CanCreateDocumentWithoutAnActiveDocument()
    {
        var workspace = new FakeWorkspaceService();
        var executor = new CadWorkspaceToolExecutor(workspace);

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
        var executor = new CadWorkspaceToolExecutor(workspace);

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

    private static object ExecuteLayerTool(CadLayerTools tools, string name, string arguments)
    {
        using var json = JsonDocument.Parse(arguments);
        return tools.Execute(name, json.RootElement);
    }

    private sealed class FakeWorkspaceService : ICadToolWorkspace
    {
        private readonly List<CadToolWorkspaceDocument> _documents = [];
        private string? _activeDocumentId;

        public void Add(string documentId, string name, bool isActive)
        {
            if (isActive)
                _activeDocumentId = documentId;
            _documents.Add(CreateDescriptor(documentId, name));
        }

        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() =>
            _documents.Select(document => document with
            {
                IsActive = document.DocumentId == _activeDocumentId
            }).ToArray();

        public CadToolWorkspaceDocument? GetActiveDocument() =>
            _activeDocumentId is null ? null : GetRequiredDocument(_activeDocumentId);

        public CadToolWorkspaceDocument GetRequiredDocument(string documentId)
        {
            var document = _documents.FirstOrDefault(candidate => candidate.DocumentId == documentId)
                ?? throw new ArgumentException($"Open document not found: {documentId}");
            return document with { IsActive = document.DocumentId == _activeDocumentId };
        }

        public CadToolWorkspaceDocument CreateDocument(string? name)
        {
            var id = $"document-{_documents.Count + 1}";
            Add(id, string.IsNullOrWhiteSpace(name) ? "Untitled" : name, isActive: true);
            return GetRequiredDocument(id);
        }

        public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken) =>
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

        private CadToolWorkspaceDocument CreateDescriptor(string documentId, string name) => new(
            documentId,
            Guid.NewGuid(),
            name,
            string.Empty,
            IsModified: false,
            IsActive: documentId == _activeDocumentId,
            EditorTab: null!);
    }
}
