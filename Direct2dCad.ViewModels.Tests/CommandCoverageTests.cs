using System.Text.Json;
using Direct2dCad.Agent;
using Direct2dCad.AI.Contracts;
using Direct2dCad.CommandLine;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Agents;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CommandCoverageTests
{
    [Fact]
    public void CatalogSchemasAndContractExposeTheSameNewCapabilities()
    {
        var tools = CadWorkspaceToolExecutor.ToolDefinitions;
        Assert.Equal(tools.Count, tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tools, tool => Assert.Equal("object", tool.Parameters.GetProperty("type").GetString()));
        foreach (var name in new[] { "boolean_regions", "set_dimension", "detach_dimension" })
            Assert.True(Assert.Single(tools, t => t.Name == name).Parameters.GetProperty("properties").TryGetProperty("document_id", out _));
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_COMMAND_AUDIT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "command-catalog.json"), JsonSerializer.Serialize(new
            {
                contract_version = CadAgentContract.Version,
                terminal_commands = new CadCommandLineService().Commands,
                tool_count = tools.Count,
                tools = tools.OrderBy(t => t.Name).Select(t => new { name = t.Name, description = t.Description, parameters = t.Parameters })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    [Fact]
    public async Task CancelDuringBooleanPreparationLeavesSourcesAndHistoryUntouched()
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Cancel Boolean").DocumentViewModel;
        var sources = Enumerable.Range(0, 100).Select(i => vm.CadEditor.Document.AddCircle(new(i % 10 * 15, i / 10 * 15), 10)).ToArray();
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        using var cancellation = new CancellationTokenSource();
        var task = new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new("mid-cancel", "boolean_regions",
            JsonSerializer.Serialize(new { operation = "union", entity_ids = sources.Select(s => s.Id.Value).ToArray() })), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.All(sources, source => Assert.False(source.IsErased));
        Assert.Equal(sources.Length, vm.CadEditor.Document.Entities.Count);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData("union", 150)]
    [InlineData("intersection", 50)]
    [InlineData("difference", 50)]
    public async Task BooleanToolsReturnExactRegionAndRestoreSourcesOnUndo(string operation, double area)
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Boolean").DocumentViewModel;
        var doc = vm.CadEditor.Document;
        var a = doc.AddRectangle(CadRectD.FromLTRB(0, 0, 10, 10));
        var b = doc.AddRectangle(CadRectD.FromLTRB(5, 0, 15, 10));
        var executor = new CadWorkspaceToolExecutor(workspace);
        var output = await ToolExecutionWorkspace.Execute(executor, "boolean_regions",
            new { operation, entity_ids = new[] { a.Id.Value, b.Id.Value }, subject_entity_id = a.Id.Value });
        var id = new EntityId(output.GetProperty("result").GetProperty("result").GetProperty("result_entity_id").GetInt64());
        var region = Assert.IsType<CadRegion>(doc.GetEntity(id));
        Assert.Equal(area, region.Area, 8);
        Assert.True(a.IsErased && b.IsErased);
        var geometry = await ToolExecutionWorkspace.Execute(executor, "get_entity_geometry", new { entity_id = id.Value });
        var exact = geometry.GetProperty("result").GetProperty("result");
        Assert.Equal("Region", exact.GetProperty("type").GetString());
        Assert.NotEmpty(exact.GetProperty("geometry").GetProperty("contours").EnumerateArray());
        vm.Undo();
        Assert.False(a.IsErased || b.IsErased); Assert.True(region.IsErased);
        vm.Redo();
        Assert.True(a.IsErased && b.IsErased); Assert.False(region.IsErased);
    }

    [Fact]
    public async Task BooleanFailureAndCancellationNeverEraseOperandsOrAddHistory()
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Protected").DocumentViewModel;
        var doc = vm.CadEditor.Document;
        var a = doc.AddCircle(default, 1); var b = doc.AddCircle(new(10, 0), 1);
        var executor = new CadWorkspaceToolExecutor(workspace);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        await ToolExecutionWorkspace.Execute(executor, "boolean_regions", new { operation = "intersection", entity_ids = new[] { a.Id.Value, b.Id.Value } }, false);
        await ToolExecutionWorkspace.Execute(executor, "boolean_regions", new { operation = "difference", entity_ids = new[] { a.Id.Value, b.Id.Value } }, false);
        b.SetLocked(true);
        await ToolExecutionWorkspace.Execute(executor, "boolean_regions", new { operation = "union", entity_ids = new[] { a.Id.Value, b.Id.Value } }, false);
        b.SetLocked(false);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(new("cancel", "boolean_regions",
            JsonSerializer.Serialize(new { operation = "union", entity_ids = new[] { a.Id.Value, b.Id.Value } })), cancellation.Token));
        Assert.Equal(2, doc.Entities.Count); Assert.False(a.IsErased || b.IsErased);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Fact]
    public async Task DimensionCreationAndEditingKeepAssociationAndUndoAllStyleFieldsTogether()
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Dimensions").DocumentViewModel;
        var line = vm.CadEditor.AddLine(default, new(100, 0));
        var executor = new CadWorkspaceToolExecutor(workspace);
        await ToolExecutionWorkspace.Execute(executor, "add_dimension", new { kind = "Aligned", source_entity_id = line.Value,
            x = 50, y = 20, shape_font = "simplex", arrow = "Closed", precision = 3 });
        var dimension = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        Assert.Equal("simplex", dimension.Definition.Style.ShapeFont);
        Assert.Equal(CadDimensionArrow.Closed, dimension.Definition.Style.Arrow);
        executor = new CadWorkspaceToolExecutor(workspace);
        await ToolExecutionWorkspace.Execute(executor, "set_dimension", new { entity_id = dimension.Id.Value,
            x = 60, y = 30, text_height = 4, arrow = "Slash", annotation_scale = 2, text_override = "CHECK" });
        Assert.Equal(new CadPointD(60, 30), dimension.Definition.Placement);
        Assert.Equal(4, dimension.Definition.Style.TextHeight); Assert.Equal("CHECK", dimension.Definition.TextOverride);
        Assert.EndsWith("CHECK", dimension.DisplayText);
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
        Assert.All(dimension.Definition.Anchors, a => Assert.NotNull(a.Reference));
        vm.Undo();
        Assert.Equal(new CadPointD(50, 20), dimension.Definition.Placement);
        Assert.Equal(2.5, dimension.Definition.Style.TextHeight);
        Assert.Equal(CadDimensionArrow.Closed, dimension.Definition.Style.Arrow);
        vm.Redo(); Assert.Equal(CadDimensionArrow.Slash, dimension.Definition.Style.Arrow);
        await ToolExecutionWorkspace.Execute(new(workspace), "set_dimension", new { entity_id = dimension.Id.Value, text_override = (string?)null });
        Assert.False(dimension.HasTextOverride);
        var geometry = await ToolExecutionWorkspace.Execute(executor, "get_entity_geometry", new { entity_id = dimension.Id.Value });
        Assert.All(geometry.GetProperty("result").GetProperty("result").GetProperty("geometry").GetProperty("anchor_references").EnumerateArray(),
            reference => Assert.Equal(line.Value, reference.GetProperty("EntityId").GetInt64()));
        await ToolExecutionWorkspace.Execute(new(workspace), "detach_dimension", new { entity_id = dimension.Id.Value });
        Assert.Equal(CadAssociationState.Detached, dimension.AssociationState);
        vm.CadEditor.SetLineGeometry(line, default, new(125, 0)); Assert.Equal(100, dimension.Measurement);
        vm.Undo(); vm.Undo();
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
        vm.CadEditor.SetLineGeometry(line, default, new(130, 0)); Assert.Equal(130, dimension.Measurement);
    }

    [Theory]
    [InlineData("{\"text_height\":-1}")]
    [InlineData("{\"arrow\":\"unknown\"}")]
    [InlineData("{\"style\":\"unknown\"}")]
    [InlineData("{\"shape_font\":\"missing\"}")]
    [InlineData("{\"annotation_scale\":\"bad\"}")]
    [InlineData("{\"x\":20}")]
    [InlineData("{\"unexpected\":20}")]
    public async Task InvalidDimensionEditsLeaveGeometryAndHistoryUntouched(string properties)
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Validation").DocumentViewModel;
        var d = vm.CadEditor.Document.AddDimension(new(CadDimensionKind.Aligned, [new(default), new(new(100, 0))], new(50, 20), new()));
        var original = d.Definition;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var json = properties.Insert(1, $"\"entity_id\":{d.Id.Value},");
        await ToolExecutionWorkspace.Execute(new(workspace), "set_dimension", json, false);
        Assert.Equal(original.Style, d.Definition.Style); Assert.Equal(original.Placement, d.Definition.Placement);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData("MOVE 1,0", 50.8, 0)]
    [InlineData("._M 1,0", 50.8, 0)]
    [InlineData("ROTATE 90 0,0", 0, 25.4)]
    [InlineData("SCALE 2 0,0", 50.8, 0)]
    [InlineData("MIRROR 0,0 90", -25.4, 0)]
    public async Task HumanTransformsUseDisplayUnitsAndReuseUndoableTools(string command, double x, double y)
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Inches").DocumentViewModel;
        vm.CadEditor.Document.DocumentSettings.SetUnit(CadUnit.Inch);
        var id = vm.CadEditor.AddLine(new(25.4, 0), new(50.8, 0));
        vm.SelectEntities([id]);
        var service = new CadToolCommandLineService(workspace);
        var result = await service.TryExecuteAsync(command);
        Assert.True(result?.Success, result?.Message);
        var line = Assert.IsType<CadLine>(vm.CadEditor.Document.GetEntity(id));
        Assert.Equal(x, line.Start.X, 8); Assert.Equal(y, line.Start.Y, 8);
        vm.Undo(); Assert.Equal(new CadPointD(25.4, 0), line.Start);
    }

    [Fact]
    public async Task HumanBooleanAndDuplicateCommandsAreDiscoverableAndKeepClipboardAliases()
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Shortcuts").DocumentViewModel;
        var a = vm.CadEditor.AddCircle(default, 10); var b = vm.CadEditor.AddCircle(new(10, 0), 10);
        vm.SelectEntities([a, b]);
        var service = new CadToolCommandLineService(workspace);
        Assert.Contains("MOVE", (await service.TryExecuteAsync("CADHELP"))!.Message);
        Assert.Contains("current display unit", (await service.TryExecuteAsync("HELP M"))!.Message);
        Assert.Contains("MOVE", service.Complete("MOV"));
        Assert.Null(await service.TryExecuteAsync("COPY")); Assert.Null(await service.TryExecuteAsync("CO"));
        Assert.True((await service.TryExecuteAsync("UNION"))!.Success);
        Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds)));
        vm.Undo(); vm.SelectEntities([a, b]);
        Assert.True((await service.TryExecuteAsync($"SUBTRACT {a.Value}"))!.Success);
        vm.Undo(); vm.SelectEntities([a]);
        Assert.True((await service.TryExecuteAsync("DUPLICATE 5,0"))!.Success);
        Assert.Equal(new CadPointD(5, 0), Assert.IsType<CadCircle>(vm.CadEditor.Document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds))).Center);
        Assert.False((await service.TryExecuteAsync("SCALE -1 0,0"))!.Success);
        Assert.False((await service.TryExecuteAsync("MOVE NaN,0"))!.Success);
        vm.Undo(); Assert.Equal(2, vm.CadEditor.Document.Entities.Values.Count(e => !e.IsErased));
    }

    [Theory]
    [InlineData("合并两个闭合实体做布尔并集", "boolean_regions")]
    [InlineData("给直线创建尺寸标注", "add_dimension")]
    [InlineData("修改标注字体和箭头", "set_dimension")]
    [InlineData("解除标注关联", "detach_dimension")]
    [InlineData("打断这条线段", "edit_curves")]
    [InlineData("创建矩形阵列", "array_entities")]
    [InlineData("导出DXF", "export_dxf")]
    public void RequestedCadToolsRemainAvailableAfterAiContextBudgeting(string prompt, string toolName)
    {
        var selected = CadAgentToolSelector.Select(prompt, CadWorkspaceToolExecutor.ToolDefinitions);
        var context = AgentRequestContextBuilder.Build("CAD assistant", [AiChatMessage.User(prompt)], selected, 8192);
        Assert.Contains(context.Tools, t => t.Name == toolName);
    }

    [Fact]
    public async Task AiQueriesErrorsAndCancellationAppearInTerminalEvenForNonactiveDocuments()
    {
        using var bus = new CadToolboxTestContext();
        using var workspace = new ToolExecutionWorkspace();
        var other = workspace.CreateDocument("Other");
        using var terminal = new CommandLineToolboxViewModel(bus.Platform, bus.Platform, new CadCommandLineService(),
            new CadToolCommandLineService(workspace), bus.GetService<IAsyncSubscriber<CadCommandActivityMessage>>(),
            bus.GetService<IAsyncSubscriber<CadInteractionActivityMessage>>(), bus.GetService<IAsyncSubscriber<CadToolActivityMessage>>());
        terminal.Attach(bus.Document); terminal.FlushPendingEntries();
        var agent = new CadAgentToolset(workspace, activityPublisher: bus.GetService<IAsyncPublisher<CadToolActivityMessage>>());
        await agent.ExecuteAsync(new("query", "get_document_summary", "{}"), default);
        await agent.ExecuteAsync(new("bad", "missing_tool", "{}"), default);
        await agent.ExecuteAsync(new("json", "get_document_summary", "[]"), default);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.ExecuteAsync(new("cancel", "list_entities", "{}"), canceled.Token));
        await bus.GetService<IAsyncPublisher<CadInteractionActivityMessage>>().PublishAsync(new(other.DocumentViewModel, "Other", "Select"));
        await bus.GetService<IAsyncPublisher<CadCommandActivityMessage>>().PublishAsync(new(other.DocumentViewModel, "Other",
            new("Move Entities", Direct2dCad.Editor.Commands.CadCommandActivityKind.Execute, Direct2dCad.Editor.Commands.CadCommandActivityScope.Document, 1, true)));
        terminal.FlushPendingEntries();
        Assert.Contains(terminal.Entries, e => e.Text.Contains("[AI] [Other] get_document_summary Completed"));
        Assert.Contains(terminal.Entries, e => e.Kind == CadCommandLineEntryKind.Error && e.Text.Contains("missing_tool Failed"));
        Assert.Contains(terminal.Entries, e => e.Kind == CadCommandLineEntryKind.Warning && e.Text.Contains("list_entities Canceled"));
        Assert.Contains(terminal.Entries, e => e.Text.Contains("[Interaction] [Other] Select"));
        Assert.Contains(terminal.Entries, e => e.Text.Contains("[Document] [Other] Execute: Move Entities"));
    }
}
