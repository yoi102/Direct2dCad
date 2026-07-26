using System.Text.Json;
using Direct2dCad.AI;
using Direct2dCad.ViewModels.AI;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAiToolSelectorTests
{
    private static readonly IReadOnlyList<AiToolDefinition> AvailableTools = CreateTools(
        "add_entities", "add_line", "add_circle", "add_arc", "add_ellipse", "add_rectangle",
        "add_polygon", "add_polyline", "add_spline", "add_text", "create_layer",
        "create_block", "insert_block", "duplicate_entities", "get_entity_geometry",
        "transform_entities", "open_document", "activate_document", "rename_document",
        "save_document", "close_document", "get_document_summary", "list_entities",
        "list_document_catalog", "list_documents", "create_document", "select_entities",
        "undo", "redo", "set_entity_common_properties");

    [Fact]
    public void Select_ComplexDrawing_PrefersBulkCreationTool()
    {
        var selected = CadAiToolSelector.Select("画一个猫的侧身图案", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "add_entities");
    }

    [Fact]
    public void Select_SingleCircle_DoesNotIncludeUnrelatedCreationTools()
    {
        var selected = CadAiToolSelector.Select("画一个圆", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "add_circle");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_entities");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_line");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_rectangle");
    }

    [Theory]
    [InlineData("绘制一段圆弧", "add_arc", "add_circle")]
    [InlineData("add a polyline", "add_polyline", "add_line")]
    [InlineData("add a spline", "add_spline", "add_line")]
    public void Select_OverlappingEntityNames_PrefersSpecificTool(
        string prompt,
        string expected,
        string excluded)
    {
        var selected = CadAiToolSelector.Select(prompt, AvailableTools);

        Assert.Contains(selected, tool => tool.Name == expected);
        Assert.DoesNotContain(selected, tool => tool.Name == excluded);
    }

    [Fact]
    public void Select_BlockRequest_IncludesBlockLifecycleTools()
    {
        var selected = CadAiToolSelector.Select("创建一个块并插入 block", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "create_block");
        Assert.Contains(selected, tool => tool.Name == "insert_block");
    }

    [Fact]
    public void Select_DocumentRequest_IncludesDocumentLifecycleTools()
    {
        var selected = CadAiToolSelector.Select("打开文件，重命名后保存并关闭文档", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "open_document");
        Assert.Contains(selected, tool => tool.Name == "activate_document");
        Assert.Contains(selected, tool => tool.Name == "rename_document");
        Assert.Contains(selected, tool => tool.Name == "save_document");
        Assert.Contains(selected, tool => tool.Name == "close_document");
    }

    private static IReadOnlyList<AiToolDefinition> CreateTools(params string[] names) =>
        names.Select(name => new AiToolDefinition(name, name, EmptySchema())).ToArray();

    private static JsonElement EmptySchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });
}

public sealed class CadAiRequestContextBuilderTests
{
    [Fact]
    public void Build_KeepsNewestUserTurnAndDropsOldTurns()
    {
        var conversation = Enumerable.Range(0, 18)
            .SelectMany(index => new[]
            {
                AiChatMessage.User($"old-user-{index}: {new string('u', 1800)}"),
                AiChatMessage.Assistant($"old-assistant-{index}: {new string('a', 1800)}")
            })
            .Append(AiChatMessage.User("newest-user-request"))
            .ToArray();

        var context = CadAiRequestContextBuilder.Build(
            "system",
            conversation,
            [],
            AiAssistantSettings.DefaultContextWindowTokens);

        Assert.Contains(context.Messages, message => message.Content == "newest-user-request");
        Assert.DoesNotContain(context.Messages, message => message.Content?.StartsWith("old-user-0:") == true);
    }

    [Fact]
    public void Build_TruncatesLargeToolResultsWithoutBreakingToolExchange()
    {
        var call = new AiToolCall("call-1", "list_entities", "{}");
        var conversation = new[]
        {
            AiChatMessage.User("inspect"),
            AiChatMessage.Assistant(null, [call]),
            AiChatMessage.Tool(call.Id, new string('x', 20000))
        };

        var context = CadAiRequestContextBuilder.Build(
            "system",
            conversation,
            [],
            AiAssistantSettings.DefaultContextWindowTokens);

        var assistant = Assert.Single(context.Messages, message => message.Role == AiChatRole.Assistant);
        var tool = Assert.Single(context.Messages, message => message.Role == AiChatRole.Tool);
        Assert.Equal(call.Id, Assert.Single(assistant.ToolCalls!).Id);
        Assert.Equal(call.Id, tool.ToolCallId);
        Assert.Contains("tool result truncated", tool.Content, StringComparison.Ordinal);
        Assert.True(tool.Content!.Length <= 6000);
    }

    [Fact]
    public void Build_AggressiveRetryProducesSmallerRequest()
    {
        var call = new AiToolCall("call-1", "list_entities", "{}");
        var conversation = new[]
        {
            AiChatMessage.User("inspect"),
            AiChatMessage.Assistant(null, [call]),
            AiChatMessage.Tool(call.Id, new string('x', 20000))
        };
        var tools = Enumerable.Range(0, 12)
            .Select(index => CreateLargeTool($"tool_{index}"))
            .ToArray();

        var normal = CadAiRequestContextBuilder.Build("system", conversation, tools, 8192);
        var aggressive = CadAiRequestContextBuilder.Build("system", conversation, tools, 8192, aggressive: true);

        Assert.True(aggressive.EstimatedPromptTokens < normal.EstimatedPromptTokens);
    }

    [Fact]
    public void Build_StaysInsideConfiguredContextBudgetWithOversizedInput()
    {
        var context = CadAiRequestContextBuilder.Build(
            "system",
            [AiChatMessage.User(new string('x', 50000))],
            Enumerable.Range(0, 20).Select(index => CreateLargeTool($"tool_{index}")).ToArray(),
            8192);

        Assert.True(context.EstimatedPromptTokens + context.MaxOutputTokens <= 8192);
        Assert.Contains(context.Messages, message => message.Role == AiChatRole.User);
    }

    [Fact]
    public void Build_RealBulkCreationSchemaFitsDefaultLmStudioContext()
    {
        const string prompt = "画一个猫的侧身图案";
        var selected = CadAiToolSelector.Select(prompt, CadAiWorkspaceToolExecutor.ToolDefinitions);

        var context = CadAiRequestContextBuilder.Build(
            "You are a CAD editing assistant.",
            [AiChatMessage.User(prompt)],
            selected,
            AiAssistantSettings.DefaultContextWindowTokens);

        Assert.Contains(context.Tools, tool => tool.Name == "add_entities");
        Assert.True(context.EstimatedPromptTokens + context.MaxOutputTokens <= 8192);
    }

    [Fact]
    public void Build_RemovesOrphanedAndIncompleteToolMessages()
    {
        var conversation = new[]
        {
            AiChatMessage.User("latest request"),
            AiChatMessage.Assistant(null, [new AiToolCall("missing", "tool", "{}")]),
            AiChatMessage.Tool("orphan", "result")
        };

        var context = CadAiRequestContextBuilder.Build("system", conversation, [], 8192);

        Assert.DoesNotContain(context.Messages, message => message.Role == AiChatRole.Assistant);
        Assert.DoesNotContain(context.Messages, message => message.Role == AiChatRole.Tool);
        Assert.Contains(context.Messages, message => message.Content == "latest request");
    }

    private static AiToolDefinition CreateLargeTool(string name)
    {
        var properties = Enumerable.Range(0, 24)
            .ToDictionary(
                index => $"property_{index}",
                index => new { type = "string", description = new string((char)('a' + index % 26), 120) });
        return new AiToolDefinition(
            name,
            new string('d', 200),
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties,
                additionalProperties = false
            }));
    }
}
