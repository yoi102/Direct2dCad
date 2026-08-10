using System.Text.Json;
using Direct2dCad.Agent;
using Direct2dCad.AI;
using Direct2dCad.ViewModels.Agents;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAgentToolSelectorTests
{
    private static readonly IReadOnlyList<AiToolDefinition> AvailableTools = CreateTools(
        "add_entities", "add_line", "add_circle", "add_arc", "add_ellipse", "add_ellipse_arc", "add_rectangle",
        "add_polygon", "add_polyline", "add_spline", "add_text", "add_shape_text",
        "list_layers", "create_layer", "rename_layer", "delete_layer",
        "set_layer_properties", "reorder_layers",
        "create_block", "insert_block", "duplicate_entities", "get_entity_geometry",
        "transform_entities", "open_document", "activate_document", "rename_document",
        "save_document", "close_document", "get_document_summary", "get_entity_statistics", "list_entities",
        "list_document_catalog", "list_documents", "create_document", "select_entities",
        "undo", "redo", "set_entity_common_properties", "set_entity_specific_properties");

    [Fact]
    public void Select_ComplexDrawing_PrefersBulkCreationTool()
    {
        var selected = CadAgentToolSelector.Select("画一个猫的侧身图案", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "add_entities");
    }

    [Fact]
    public void Select_SingleCircle_DoesNotIncludeUnrelatedCreationTools()
    {
        var selected = CadAgentToolSelector.Select("画一个圆", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "add_circle");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_entities");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_line");
        Assert.DoesNotContain(selected, tool => tool.Name == "add_rectangle");
    }

    [Theory]
    [InlineData("how many entities are there and what types exist")]
    [InlineData("\u6709\u591a\u5c11\u5b9e\u4f53\uff0c\u6709\u4ec0\u4e48\u7c7b\u578b")]
    public void Select_EntityInventoryQuestion_PrioritizesUnfilteredStatistics(string prompt)
    {
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.NotEmpty(selected);
        Assert.Equal("get_entity_statistics", selected[0].Name);
    }

    [Theory]
    [InlineData("find circles with radius greater than 10")]
    [InlineData("\u67e5\u627e\u534a\u5f84\u5927\u4e8e10\u7684\u5706")]
    public void Select_EntityFeatureQuery_PrioritizesStructuredEntityQuery(string prompt)
    {
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.NotEmpty(selected);
        Assert.Equal("list_entities", selected[0].Name);
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
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.Contains(selected, tool => tool.Name == expected);
        Assert.DoesNotContain(selected, tool => tool.Name == excluded);
    }

    [Theory]
    [InlineData("add an elliptical arc", "add_ellipse_arc", "add_ellipse")]
    [InlineData("add shape text", "add_shape_text", "add_text")]
    public void Select_NewOverlappingEntityNames_PrefersSpecificTool(
        string prompt,
        string expected,
        string excluded)
    {
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.Contains(selected, tool => tool.Name == expected);
        Assert.DoesNotContain(selected, tool => tool.Name == excluded);
    }

    [Theory]
    [InlineData("change the text font")]
    [InlineData("set image opacity")]
    [InlineData("make the text inverted")]
    [InlineData("\u4fee\u6539\u6587\u672c\u5b57\u4f53")]
    [InlineData("\u8bbe\u7f6e\u56fe\u50cf\u900f\u660e\u5ea6")]
    public void Select_TypeSpecificPropertyIntent_IncludesSpecificPropertyTool(string prompt)
    {
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "set_entity_specific_properties");
    }

    [Fact]
    public void Select_BlockRequest_IncludesBlockLifecycleTools()
    {
        var selected = CadAgentToolSelector.Select("创建一个块并插入 block", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "create_block");
        Assert.Contains(selected, tool => tool.Name == "insert_block");
    }

    [Fact]
    public void Select_DocumentRequest_IncludesDocumentLifecycleTools()
    {
        var selected = CadAgentToolSelector.Select("打开文件，重命名后保存并关闭文档", AvailableTools);

        Assert.Contains(selected, tool => tool.Name == "open_document");
        Assert.Contains(selected, tool => tool.Name == "activate_document");
        Assert.Contains(selected, tool => tool.Name == "rename_document");
        Assert.Contains(selected, tool => tool.Name == "save_document");
        Assert.Contains(selected, tool => tool.Name == "close_document");
    }

    [Theory]
    [InlineData("删除图层", "delete_layer")]
    [InlineData("レイヤーを削除", "delete_layer")]
    [InlineData("重命名图层", "rename_layer")]
    [InlineData("锁定图层并设置颜色", "set_layer_properties")]
    [InlineData("调整图层顺序", "reorder_layers")]
    public void Select_LayerIntent_PrioritizesRequestedLayerTool(string prompt, string expectedTool)
    {
        var selected = CadAgentToolSelector.Select(prompt, AvailableTools);

        Assert.NotEmpty(selected);
        Assert.Equal(expectedTool, selected[0].Name);
        Assert.Contains(selected, tool => tool.Name == "list_layers");
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

public sealed class AgentRequestContextBuilderTests
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

        var context = AgentRequestContextBuilder.Build(
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

        var context = AgentRequestContextBuilder.Build(
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

        var normal = AgentRequestContextBuilder.Build("system", conversation, tools, 8192);
        var aggressive = AgentRequestContextBuilder.Build("system", conversation, tools, 8192, aggressive: true);

        Assert.True(aggressive.EstimatedPromptTokens < normal.EstimatedPromptTokens);
    }

    [Fact]
    public void Build_StaysInsideConfiguredContextBudgetWithOversizedInput()
    {
        var context = AgentRequestContextBuilder.Build(
            "system",
            [AiChatMessage.User(new string('x', 50000))],
            Enumerable.Range(0, 20).Select(index => CreateLargeTool($"tool_{index}")).ToArray(),
            8192);

        Assert.True(context.EstimatedPromptTokens + context.MaxOutputTokens <= 8192);
        Assert.Contains(context.Messages, message => message.Role == AiChatRole.User);
    }

    [Fact]
    public void Build_TruncatesLargeTextAttachmentInsideContentParts()
    {
        var message = AiChatMessage.User(
            "summarize the attached file",
            [AiChatContentPart.FileText(
                "notes.txt",
                "text/plain",
                new string('x', 100_000))]);

        var context = AgentRequestContextBuilder.Build(
            "system",
            [message],
            [],
            4096);

        var user = Assert.Single(context.Messages, item => item.Role == AiChatRole.User);
        Assert.Contains(
            "content truncated to fit the model context window",
            user.ContentParts![0].Text,
            StringComparison.Ordinal);
        Assert.True(context.EstimatedPromptTokens + context.MaxOutputTokens <= 4096);
    }

    [Fact]
    public void Build_RealBulkCreationSchemaFitsDefaultLmStudioContext()
    {
        const string prompt = "画一个猫的侧身图案";
        var selected = CadAgentToolSelector.Select(prompt, CadWorkspaceToolExecutor.ToolDefinitions);

        var context = AgentRequestContextBuilder.Build(
            "You are a CAD editing assistant.",
            [AiChatMessage.User(prompt)],
            selected,
            AiAssistantSettings.DefaultContextWindowTokens);

        Assert.Contains(context.Tools, tool => tool.Name == "add_entities");
        Assert.True(context.EstimatedPromptTokens + context.MaxOutputTokens <= 8192);
    }

    [Fact]
    public void Build_RealLayerDeletionSchemaKeepsRequestedActionWithinDefaultContext()
    {
        const string prompt = "删除图层 Construction 以及其中的所有实体";
        var selected = CadAgentToolSelector.Select(prompt, CadWorkspaceToolExecutor.ToolDefinitions);

        var context = AgentRequestContextBuilder.Build(
            "You are a CAD editing assistant.",
            [AiChatMessage.User(prompt)],
            selected,
            AiAssistantSettings.DefaultContextWindowTokens);

        Assert.Equal("delete_layer", context.Tools[0].Name);
        Assert.Contains(context.Tools, tool => tool.Name == "list_layers");
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

        var context = AgentRequestContextBuilder.Build("system", conversation, [], 8192);

        Assert.DoesNotContain(context.Messages, message => message.Role == AiChatRole.Assistant);
        Assert.DoesNotContain(context.Messages, message => message.Role == AiChatRole.Tool);
        Assert.Contains(context.Messages, message => message.Content == "latest request");
    }

    [Fact]
    public void Build_WithEmptyConversationReturnsOnlySystemMessage()
    {
        var context = AgentRequestContextBuilder.Build(
            "system",
            [],
            [],
            AiAssistantSettings.DefaultContextWindowTokens);

        var system = Assert.Single(context.Messages);
        Assert.Equal(AiChatRole.System, system.Role);
        Assert.Equal("system", system.Content);
        Assert.Empty(context.Tools);
        Assert.Equal(1024, context.MaxOutputTokens);
    }

    [Fact]
    public void Build_WithNoUserTurnsKeepsNewestHistoryAndRemovesOrphans()
    {
        var conversation = new[]
        {
            AiChatMessage.Assistant("old response"),
            AiChatMessage.Tool("orphan", "orphan result"),
            AiChatMessage.Assistant("new response")
        };

        var context = AgentRequestContextBuilder.Build("system", conversation, [], 8192);

        Assert.Contains(context.Messages, message => message.Content == "new response");
        Assert.DoesNotContain(context.Messages, message => message.Content == "orphan result");
    }

    [Fact]
    public void EstimateMessageTokensCountsTextImageAndToolCallContent()
    {
        var message = AiChatMessage.User(
            "prompt",
            [
                AiChatContentPart.TextPart("attached text"),
                AiChatContentPart.Image("data:image/png;base64,AA==")
            ]) with
        {
            ToolCalls = [new AiToolCall("call", "inspect", "{}")]
        };

        var tokens = AgentRequestContextBuilder.EstimateMessageTokens(message);

        Assert.True(tokens >= 1024);
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
