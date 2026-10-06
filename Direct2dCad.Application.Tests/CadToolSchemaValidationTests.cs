using System.Text.Json;
using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Application.Tests;

public sealed class CadToolSchemaValidationTests
{
    [Theory]
    [InlineData("add_line", "{\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0,\"visibile\":false}", "$.visibile")]
    [InlineData("add_line", "{\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0,\"visibile\":null}", "$.visibile")]
    [InlineData("add_line", "{\"x1\":null,\"y1\":0,\"x2\":10,\"y2\":0}", "$.x1")]
    [InlineData("set_entity_common_properties", "{\"entity_ids\":[1],\"name\":\"Changed\",\"visibile\":false}", "$.visibile")]
    [InlineData("add_circle", "{\"center_x\":0,\"center_y\":0,\"radius\":0}", "$.radius")]
    [InlineData("add_circle", "{\"center_x\":0,\"center_y\":0,\"radius\":1e999}", "$.radius")]
    [InlineData("add_line", "{\"x1\":\"zero\",\"y1\":0,\"x2\":10,\"y2\":0}", "$.x1")]
    [InlineData("add_line", "{\"x1\":0,\"x1\":20,\"y1\":0,\"x2\":10,\"y2\":0}", "$.x1")]
    [InlineData("add_line", "{\"x1\":0,\"y1\":0,\"x2\":10}", "$.y2")]
    [InlineData("add_entities", "{\"entities\":[{\"type\":\"line\",\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0},{\"type\":\"line\",\"x1\":20,\"y1\":0,\"x2\":30,\"y2\":0,\"visibile\":false}]}", "$.entities[1].visibile")]
    [InlineData("add_entities", "{\"entities\":[{\"type\":\"unknown\",\"x1\":0}]}", "$.entities[0].type")]
    [InlineData("add_entities", "{\"entities\":[{\"type\":\"circle\",\"center_x\":0,\"center_y\":0,\"radius\":1,\"fill\":{\"mode\":\"solid\",\"colour\":\"red\"}}]}", "$.entities[0].fill.colour")]
    [InlineData("select_entities", "{\"entity_ids\":[1,1]}", "$.entity_ids")]
    [InlineData("set_entity_common_properties", "{\"entity_ids\":[1],\"locked\":\"false\"}", "$.locked")]
    [InlineData("set_layout_paper", "{\"layout_id\":1,\"unknown\":true}", "$.unknown")]
    [InlineData("capture_view", "{\"maximum_size\":-1}", "$")]
    public async Task InvalidArgumentsFailBeforeAnyDocumentOrHistoryChanges(string tool, string arguments, string errorPath)
    {
        using var workspace = new Workspace();
        var editor = workspace.Session.CadEditor;
        editor.AddLine(default, new(5, 0));
        var history = editor.CreateDocumentHistorySnapshot();
        var version = editor.DocumentChangeVersion;
        using var result = JsonDocument.Parse(await new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new("invalid", tool, arguments), default));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(errorPath, result.RootElement.GetProperty("error").GetString());
        Assert.Equal(version, editor.DocumentChangeVersion);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Single(editor.Document.Entities);
        Assert.True(editor.Document.Entities.Values.Single().IsVisible);
    }

    [Theory]
    [InlineData("{\"x\":1,\"z\":2}", "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"}},\"additionalProperties\":false}")]
    [InlineData("{\"mode\":\"style\"}", "{\"if\":{\"properties\":{\"mode\":{\"const\":\"style\"}}},\"then\":{\"required\":[\"style\"]}}")]
    [InlineData("3", "{\"oneOf\":[{\"type\":\"number\"},{\"type\":\"integer\"}]}")]
    [InlineData("[]", "{\"type\":\"array\",\"minItems\":1}")]
    [InlineData("[1,2]", "{\"type\":\"array\",\"maxItems\":1}")]
    [InlineData("[1,1.0]", "{\"type\":\"array\",\"uniqueItems\":true}")]
    [InlineData("\"\"", "{\"type\":\"string\",\"minLength\":1}")]
    [InlineData("{\"x\":1,\"y\":2}", "{\"not\":{\"required\":[\"x\",\"y\"]}}")]
    [InlineData("{\"x\":1}", "{\"anyOf\":[{\"required\":[\"a\"]},{\"required\":[\"b\"]}]}")]
    public void SchemaCombinatorsAndLimitsAreEnforced(string value, string schema)
    {
        using var data = JsonDocument.Parse(value); using var contract = JsonDocument.Parse(schema);
        Assert.Throws<ArgumentException>(() => CadToolSchemaValidator.Validate(data.RootElement, contract.RootElement));
    }

    [Fact]
    public async Task ValidNullableDimensionOverrideAndNestedCreationRemainSupported()
    {
        using var workspace = new Workspace();
        var executor = new CadWorkspaceToolExecutor(workspace);
        using var result = JsonDocument.Parse(await executor.ExecuteAsync(new("valid", "add_entities",
            """{"entities":[{"type":"line","x1":0,"y1":0,"x2":10,"y2":0,"visible":false},{"type":"circle","center_x":20,"center_y":0,"radius":2,"fill":{"mode":"solid","color":"red"}}]}"""), default));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), result.RootElement.ToString());
        Assert.Equal(2, workspace.Session.CadEditor.Document.Entities.Count);
        using var nullable = JsonDocument.Parse("""{"entity_id":1,"text_override":null}""");
        CadToolSchemaValidator.Validate("set_dimension", nullable.RootElement, CadWorkspaceToolExecutor.ToolDefinitions);
    }

    [Fact]
    public void CapabilityCatalogDefaultsToUnavailableHostServicesAndCoversAllDefinitions()
    {
        var contract = JsonSerializer.SerializeToElement(CadAgentContract.CreateCapabilities([], null, false));
        Assert.False(contract.GetProperty("host_capabilities").GetProperty("view_capture").GetBoolean());
        Assert.False(contract.GetProperty("host_capabilities").GetProperty("printing").GetBoolean());
        var grouped = contract.GetProperty("tool_groups").EnumerateObject().SelectMany(group => group.Value.EnumerateArray()).Select(item => item.GetString()).Order().ToArray();
        Assert.Equal(CadWorkspaceToolExecutor.ToolDefinitions.Select(tool => tool.Name).Order(), grouped);
    }

    [Theory]
    [InlineData("DashDot", "Round")]
    [InlineData("dash_dot", "round")]
    [InlineData("DASH-DOT", "ROUND")]
    public async Task LegacyEnumSpellingsNormalizeAndOptionalNullDoesNotChangeAppearance(string dash, string cap)
    {
        using var workspace = new Workspace();
        var editor = workspace.Session.CadEditor;
        var id = editor.AddLine(default, new(10, 0));
        var executor = new CadWorkspaceToolExecutor(workspace);
        var args = JsonSerializer.Serialize(new { entity_ids = new[] { id.Value }, dash_style = dash, dash_cap = cap });
        using var result = JsonDocument.Parse(await executor.ExecuteAsync(new("legacy", "set_entity_stroke_style", args), default));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), result.RootElement.ToString());
        Assert.Equal(Direct2dCad.Db.Data.Entities.CadStrokeDashStyle.DashDot, editor.Document.GetEntity(id).StrokeStyle.DashStyle);
        using var renamed = JsonDocument.Parse(await executor.ExecuteAsync(new("null", "set_entity_common_properties",
            """{"entity_ids":[1],"name":"Renamed","color":null}"""), default));
        Assert.True(renamed.RootElement.GetProperty("success").GetBoolean(), renamed.RootElement.ToString());
        Assert.Equal("Renamed", editor.Document.GetEntity(id).Name);
    }

    [Fact]
    public void NormalizationPreservesNullableClearAndChecksNestedEnumsWithoutDroppingUnknownNulls()
    {
        using var clear = JsonDocument.Parse("""{"entity_id":1,"text_override":null}""");
        var normalized = CadToolSchemaValidator.NormalizeAndValidate("set_dimension", clear.RootElement, CadWorkspaceToolExecutor.ToolDefinitions);
        Assert.Equal(JsonValueKind.Null, normalized.GetProperty("text_override").ValueKind);
        using var data = JsonDocument.Parse("""{"entities":[{"type":"Circle","center_x":0,"center_y":0,"radius":2,"fill":{"mode":"GRADIENT","gradient_kind":"Radial","stops":[{"offset":0,"color":"red"},{"offset":1,"color":"blue"}]}}]}""");
        normalized = CadToolSchemaValidator.NormalizeAndValidate("add_entities", data.RootElement, CadWorkspaceToolExecutor.ToolDefinitions);
        Assert.Equal("circle", normalized.GetProperty("entities")[0].GetProperty("type").GetString());
        Assert.Equal("radial", normalized.GetProperty("entities")[0].GetProperty("fill").GetProperty("gradient_kind").GetString());
    }

    [Fact]
    public void PublicDocumentExecutorCannotBypassValidation()
    {
        using var session = new CadToolDocumentSession(CadDocument.Create("Direct"));
        var executor = new CadDocumentToolExecutor(session, Guid.NewGuid());
        using var result = JsonDocument.Parse(executor.Execute(new("invalid", "add_line", """{"x1":0,"y1":0,"x2":10,"y2":0,"visibile":false}""")));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("$.visibile", result.RootElement.GetProperty("error").GetString());
        Assert.Empty(session.CadEditor.Document.Entities);
        Assert.False(session.CadEditor.DocumentCommands.CanUndo);
    }

    private sealed class Workspace : ICadToolWorkspace, ICadWorkspaceDocument, IDisposable
    {
        private readonly CadToolWorkspaceDocument _document;
        public Workspace() { Session = new(CadDocument.Create("Validation")); _document = new("test", Session.CadEditor.Document.Id.Value, "Validation", "", false, true, this); }
        public CadToolDocumentSession Session { get; }
        ICadToolDocumentSession ICadWorkspaceDocument.Session => Session;
        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => [_document];
        public CadToolWorkspaceDocument? GetActiveDocument() => _document;
        public CadToolWorkspaceDocument GetRequiredDocument(string id) => id == "test" ? _document : throw new ArgumentException("Document missing");
        public CadToolWorkspaceDocument CreateDocument(string? name) => throw new NotSupportedException();
        public bool ActivateDocument(string id) => throw new NotSupportedException();
        public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public bool RenameDocument(string id, string name) => throw new NotSupportedException();
        public Task<bool> SaveDocumentAsync(string id, string? path, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> CloseDocumentAsync(string id) => throw new NotSupportedException();
        public void Load(CadDocument document, string path) => throw new NotSupportedException();
        public Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default) => operation(token);
        public void Dispose() => Session.Dispose();
    }
}
