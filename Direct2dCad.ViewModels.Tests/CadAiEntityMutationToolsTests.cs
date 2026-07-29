using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.AI;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAiEntityMutationToolsTests
{
    [Fact]
    public void TextProperties_MixedTextTypesExecuteAndUndoAsOneBatch()
    {
        var document = CadDocument.Create("AI text");
        var text = document.AddText("old text", CadPointD.Origin, 5);
        var shapeText = document.AddShapeText("old shape", new CadPointD(10, 0), 5);
        var editor = new CadEditor(document);
        var batchId = Guid.NewGuid();
        var tools = CreateTools(document, editor, batchId);

        Execute(tools, "set_text_properties", $$"""
            {
              "entity_ids": [{{text.Id.Value}}, {{shapeText.Id.Value}}],
              "text": "updated",
              "inverted": true,
              "inverted_margin_factor": 0.25
            }
            """);

        Assert.Equal("updated", text.Text);
        Assert.Equal("updated", shapeText.Text);
        Assert.True(text.IsInverted);
        Assert.True(shapeText.IsInverted);
        Assert.Equal(0.25, text.InvertedMarginFactor);
        Assert.Equal(0.25, shapeText.InvertedMarginFactor);

        editor.UndoBatch(batchId);

        Assert.Equal("old text", text.Text);
        Assert.Equal("old shape", shapeText.Text);
        Assert.False(text.IsInverted);
        Assert.False(shapeText.IsInverted);
    }

    [Fact]
    public void ShapeFontAndBlockPropertiesExecuteAndUndoAsOneBatch()
    {
        var document = CadDocument.Create("AI specific properties");
        var shapeText = document.AddShapeText("shape", CadPointD.Origin, 5);
        var firstBlock = document.CreateBlockDefinition("First", CadPointD.Origin);
        var secondBlock = document.CreateBlockDefinition("Second", new CadPointD(1, 2));
        var reference = document.AddBlockReference(firstBlock, new CadPointD(20, 30));
        var editor = new CadEditor(document);
        var batchId = Guid.NewGuid();
        var tools = CreateTools(document, editor, batchId);

        Execute(tools, "set_text_properties", $$"""
            {
              "entity_ids": [{{shapeText.Id.Value}}],
              "shape_font": "simplex"
            }
            """);
        Execute(tools, "set_block_reference_definition", $$"""
            {
              "entity_ids": [{{reference.Id.Value}}],
              "block": "Second"
            }
            """);
        Execute(tools, "set_block_definition_base_point", """
            {
              "block": "Second",
              "base_x": 40,
              "base_y": 50
            }
            """);

        Assert.Equal(CadShapeFontId.Simplex, shapeText.ShapeFontId);
        Assert.Equal(secondBlock, reference.DefinitionBlockId);
        Assert.Equal(new CadPointD(40, 50), document.GetBlock(secondBlock).BasePoint);

        editor.UndoBatch(batchId);

        Assert.Equal(CadShapeFontRegistry.DefaultShapeFontId, shapeText.ShapeFontId);
        Assert.Equal(firstBlock, reference.DefinitionBlockId);
        Assert.Equal(new CadPointD(1, 2), document.GetBlock(secondBlock).BasePoint);
    }

    [Fact]
    public void EmbeddedDataReplacementAndPropertyReadExposeCurrentMetadata()
    {
        var document = CadDocument.Create("AI embedded data");
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(0, 0, 10, 10),
            [1, 2, 3],
            contentType: "application/original",
            sourceName: "old.ole");
        var editor = new CadEditor(document);
        var batchId = Guid.NewGuid();
        var tools = CreateTools(document, editor, batchId);
        var replacement = Convert.ToBase64String([9, 8, 7, 6]);

        Execute(tools, "replace_embedded_entity_data", $$"""
            {
              "entity_ids": [{{ole.Id.Value}}],
              "data_base64": "{{replacement}}",
              "content_type": "application/updated",
              "source_name": "new.ole"
            }
            """);
        var properties = Execute(tools, "get_entity_properties", $$"""
            { "entity_ids": [{{ole.Id.Value}}] }
            """);
        var json = JsonSerializer.Serialize(properties);

        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ole.CopyOleBytes());
        Assert.Contains("\"byte_count\":4", json, StringComparison.Ordinal);
        Assert.Contains("\"Opacity\":1", json, StringComparison.Ordinal);

        editor.UndoBatch(batchId);
        Assert.Equal(new byte[] { 1, 2, 3 }, ole.CopyOleBytes());
        Assert.Equal("application/original", ole.ContentType);
    }

    private static CadAiEntityMutationTools CreateTools(
        CadDocument document,
        CadEditor editor,
        Guid batchId) => new(
        document,
        ResolveIds,
        command => editor.ExecuteInBatch(command, batchId),
        _ => { });

    private static object Execute(CadAiEntityMutationTools tools, string name, string arguments)
    {
        using var json = JsonDocument.Parse(arguments);
        return tools.Execute(name, json.RootElement);
    }

    private static EntityId[] ResolveIds(JsonElement arguments) =>
        arguments.GetProperty("entity_ids")
            .EnumerateArray()
            .Select(value => new EntityId(value.GetInt64()))
            .ToArray();
}
