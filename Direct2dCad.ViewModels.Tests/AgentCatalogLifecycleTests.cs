using System.Text.Json;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.ViewModels.Tools;
using static Direct2dCad.ViewModels.Tests.ToolExecutionWorkspace;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AgentCatalogLifecycleTests
{
    [Theory]
    [InlineData("create_graphic_style", "{\"name\":\"Fixture\",\"color\":\"#FF0000\",\"line_weight\":0.5}")]
    [InlineData("create_text_style", "{\"name\":\"Fixture\",\"font_family\":\"Arial\",\"text_height\":5,\"width_factor\":1.2,\"bold\":true,\"italic\":true}")]
    [InlineData("create_fill_style", "{\"name\":\"Fixture\",\"mode\":\"solid\",\"color\":\"#00FF00\"}")]
    [InlineData("create_fill_style", "{\"name\":\"Fixture\",\"mode\":\"gradient\",\"gradient_kind\":\"radial\",\"stops\":[{\"offset\":0,\"color\":\"#FF0000\"},{\"offset\":1,\"color\":\"#0000FF\"}]}")]
    public async Task SharedStylesCanBeCreatedListedRenamedDeletedAndRestored(string tool, string arguments)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Styles").DocumentViewModel.CadEditor;
        var initial = editor.Document.Styles.Count;
        await Execute(new(workspace), tool, arguments);
        var style = editor.Document.Styles.Values.Single(style => style.Name == "Fixture");
        var id = style.Id;
        var result = Payload(await Execute(new(workspace), "list_styles", new { }));
        Assert.Contains(result.GetProperty("styles").EnumerateArray(), item => item.GetProperty("id").GetInt64() == id.Value);
        await Execute(new(workspace), "list_document_catalog", new { });
        await Execute(new(workspace), "rename_style", new { style = "Fixture", new_name = "Updated" });
        Assert.Equal("Updated", editor.Document.Styles[id].Name);
        await Execute(new(workspace), "delete_style", new { style = "Updated" }, success: false);
        await Execute(new(workspace), "delete_style", new { style = "Updated", confirm = true });
        Assert.False(editor.Document.Styles.ContainsKey(id));
        editor.Undo();
        Assert.Equal("Updated", editor.Document.Styles[id].Name);
        editor.Undo();
        Assert.Equal("Fixture", editor.Document.Styles[id].Name);
        editor.Undo();
        Assert.Equal(initial, editor.Document.Styles.Count);
        editor.Redo();
        Assert.Equal("Fixture", editor.Document.Styles[id].Name);
    }

    [Fact]
    public async Task ReferencedLineTypesAndHatchPatternsCannotBeDeleted()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Catalog").DocumentViewModel.CadEditor;
        await Execute(new(workspace), "create_line_type", new { name = "Dashes", dash_pattern = new[] { 3, -2, 1, -2 } });
        var line = editor.Document.LineTypes.Values.Single(item => item.Name == "Dashes");
        await Execute(new(workspace), "rename_line_type", new { line_type = "Dashes", new_name = "Pattern" });
        await Execute(new(workspace), "create_graphic_style", new { name = "Stroke", color = "#FFFFFF", line_type_id = line.Id.Value });
        await Execute(new(workspace), "delete_line_type", new { line_type = "Pattern", confirm = true }, success: false);
        await Execute(new(workspace), "delete_style", new { style = "Stroke", confirm = true });
        await Execute(new(workspace), "delete_line_type", new { line_type = "Pattern" }, success: false);
        await Execute(new(workspace), "delete_line_type", new { line_type = "Pattern", confirm = true });
        Assert.False(editor.Document.LineTypes.ContainsKey(line.Id));
        editor.Undo();
        Assert.Equal("Pattern", editor.Document.GetLineType(line.Id).Name);

        await Execute(new(workspace), "create_hatch_pattern", new { name = "Hatch", lines = new[] {
            new { angle_degrees = 45, origin = new { x = 0, y = 0 }, offset = new { x = 0, y = 2 }, dash_pattern = new[] { 2, -1 } } } });
        var pattern = editor.Document.HatchPatterns.Values.Single(item => item.Name == "Hatch");
        await Execute(new(workspace), "create_fill_style", new { name = "Fill", mode = "hatch", pattern = "Hatch", color = "#00FF00", scale = 2, angle_degrees = 30 });
        await Execute(new(workspace), "delete_hatch_pattern", new { pattern = "Hatch", confirm = true }, success: false);
        await Execute(new(workspace), "list_styles", new { });
        await Execute(new(workspace), "delete_style", new { style = "Fill", confirm = true });
        await Execute(new(workspace), "delete_hatch_pattern", new { pattern = "Hatch" }, success: false);
        await Execute(new(workspace), "delete_hatch_pattern", new { pattern = "Hatch", confirm = true });
        Assert.False(editor.Document.HatchPatterns.ContainsKey(pattern.Id));
        editor.Undo();
        Assert.True(editor.Document.HatchPatterns.ContainsKey(pattern.Id));
    }

    [Fact]
    public async Task BlockCreationInsertionEditingRenameAndDeletionPreserveReferences()
    {
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Blocks").DocumentViewModel;
        var editor = vm.CadEditor;
        var line = editor.AddLine(new(0, 0), new(10, 10));
        var created = Payload(await Execute(new(workspace), "create_block", new { entity_ids = new[] { line.Value }, name = "Part", base_x = 0, base_y = 0 }));
        var blockId = new BlockId(created.GetProperty("block_id").GetInt64());
        var firstReference = new EntityId(created.GetProperty("reference_entity_id").GetInt64());
        Assert.Equal(blockId, editor.Document.GetEntity(line).OwnerBlockId);
        await Execute(new(workspace), "insert_block", new { block = "Part", x = 20, y = 30, scale_x = -2, scale_y = 3, rotation_degrees = 30 });
        var secondReference = Assert.Single(editor.Selection.EntityIds);
        var reference = Assert.IsType<CadBlockReference>(editor.Document.GetEntity(secondReference));
        Assert.Equal(-2, reference.ScaleX);
        Assert.Equal(3, reference.ScaleY);
        var renamed = Payload(await Execute(new(workspace), "rename_block", new { block = "Part", new_name = "Updated" }));
        Assert.Equal("Part", renamed.GetProperty("old_name").GetString());
        Assert.Equal("Updated", renamed.GetProperty("new_name").GetString());
        await Execute(new(workspace), "list_blocks", new { });
        await Execute(new(workspace), "edit_block", new { block = "Updated" });
        Assert.Equal(blockId, editor.ActiveOwnerBlockId);
        await Execute(new(workspace), "exit_block_edit", new { });
        Assert.Equal(BlockId.ModelSpace, editor.ActiveOwnerBlockId);
        await Execute(new(workspace), "delete_block", new { block = "Updated", confirm = true }, success: false);
        editor.DeleteEntities([firstReference, secondReference]);
        await Execute(new(workspace), "delete_block", new { block = "Updated" }, success: false);
        await Execute(new(workspace), "delete_block", new { block = "Updated", confirm = true });
        Assert.False(editor.Document.Blocks.ContainsKey(blockId));
        editor.Undo();
        Assert.True(editor.Document.Blocks.ContainsKey(blockId));
        editor.Undo();
        Assert.False(reference.IsErased);
    }

    private static JsonElement Payload(JsonElement response) => response.GetProperty("result").GetProperty("result");
}
