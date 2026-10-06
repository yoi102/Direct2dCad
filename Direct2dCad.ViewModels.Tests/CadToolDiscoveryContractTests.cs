using Direct2dCad.Agent;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Tools;
using Direct2dCad.ViewModels.Agents;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadToolDiscoveryContractTests
{
    [Theory]
    [InlineData("画一个半径10毫米的圆", "add_circle")]
    [InlineData("draw a 10 millimeter circle", "add_circle")]
    [InlineData("将所选直线移动10毫米", "move_entities")]
    [InlineData("创建文字样式", "create_text_style")]
    [InlineData("创建填充样式", "create_fill_style")]
    [InlineData("修改文字样式", "set_text_style_properties")]
    [InlineData("修改图形样式", "set_graphic_style_properties")]
    [InlineData("重命名线型", "rename_line_type")]
    [InlineData("删除填充样式", "delete_style")]
    [InlineData("插入OLE对象", "add_ole_object")]
    [InlineData("修改嵌入对象内容", "set_ole_object_data")]
    [InlineData("画一个圆和一段圆弧", "add_circle")]
    [InlineData("画一个圆和一段圆弧", "add_arc")]
    [InlineData("画一条直线和一条多段线", "add_line")]
    [InlineData("画一条直线和一条多段线", "add_polyline")]
    [InlineData("创建布局", "create_layout")]
    [InlineData("调整布局视口", "set_layout_viewport")]
    [InlineData("重新关联标注", "reassociate_dimension")]
    [InlineData("截取当前画面", "capture_view")]
    [InlineData("打印图纸", "print_document")]
    public void RequestsKeepTheirOperationDespiteUnitsLanguageOrCompoundGeometry(string prompt, string expected)
    {
        var selected = CadAgentToolSelector.Select(prompt, CadWorkspaceToolExecutor.ToolDefinitions);
        Assert.Contains(expected, selected.Select(tool => tool.Name));
        // The normal 8K budget must still leave the operation executable.
        var context = AgentRequestContextBuilder.Build("CAD assistant", [AiChatMessage.User(prompt)], selected, 8192);
        Assert.Contains(expected, context.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void EveryRegisteredToolCanBeRequestedByItsExactName()
    {
        foreach (var tool in CadWorkspaceToolExecutor.ToolDefinitions)
            Assert.Contains(tool.Name, CadAgentToolSelector.Select(tool.Name, CadWorkspaceToolExecutor.ToolDefinitions).Select(item => item.Name));
    }

    [Fact]
    public void CatalogGroupsCoverEveryRegisteredToolExactlyOnceIncludingFutureEntries()
    {
        var definitions = CadWorkspaceToolExecutor.ToolDefinitions;
        var future = new AiToolDefinition("future_operation", "future", definitions[0].Parameters);
        var groups = CadToolCatalog.Groups([.. definitions, future]);
        Assert.Equal(definitions.Select(tool => tool.Name).Append(future.Name).Order(), groups.Values.SelectMany(names => names).Order());
        Assert.Contains("add_ellipse_arc", groups["create"]);
        Assert.Contains("delete_entities", groups["geometry"]);
        Assert.Contains("change_entity_layer", groups["organization"]);
    }
}
