using Direct2dCad.AI.Contracts;
using Direct2dCad.ViewModels.Agents;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAgentToolSelectorWordTests
{
    [Theory]
    [InlineData("draw a plate with a hole", "ole")]
    [InlineData("add the whole outline", "ole")]
    [InlineData("in this context", "text")]
    [InlineData("search the drawing", "arc")]
    [InlineData("reset the view", "set")]
    [InlineData("send it to the company", "pan")]
    [InlineData("direct the result", "rect")]
    public void EnglishKeywordsDoNotMatchInsideOtherWords(string prompt, string term) =>
        Assert.False(CadAgentToolSelector.Matches(prompt, term));

    [Theory]
    [InlineData("draw three circles", "circle")]
    [InlineData("switch the units to inches", "inch")]
    [InlineData("the lines are moved", "move")]
    [InlineData("drawing an ole object", "ole")]
    [InlineData("画一个circle", "circle")]
    [InlineData("set_entity_fill please", "set_entity_fill")]
    public void EnglishKeywordsMatchWholeWordsAndCommonInflections(string prompt, string term) =>
        Assert.True(CadAgentToolSelector.Matches(prompt, term));

    [Fact]
    public void AHoleInADrawingRequestDoesNotPrioritiseOleTools()
    {
        var selected = Select("draw a 100x50 rectangle with a 10 mm hole in the middle");
        Assert.DoesNotContain("add_ole_object", selected);
        Assert.Contains("add_rectangle", selected); // the requested drawing tool is still chosen
    }

    [Theory]
    [InlineData("円を描いて", "add_circle")]
    [InlineData("直線を作図して", "add_line")]
    [InlineData("楕円を描画", "add_ellipse")]
    [InlineData("長方形を描いて", "add_rectangle")]
    public void JapaneseDrawingRequestsSelectTheMatchingCreationTool(string prompt, string tool) =>
        Assert.Contains(tool, Select(prompt));

    [Fact]
    public void ChineseDescribeIsNotTreatedAsDrawing() =>
        Assert.DoesNotContain("add_entities", Select("描述一下当前文档"));

    private static IReadOnlyList<string> Select(string prompt)
    {
        var catalog = Direct2dCad.Application.Tools.CadWorkspaceToolExecutor.ToolDefinitions;
        return CadAgentToolSelector.Select(prompt, catalog).Select(tool => tool.Name).ToArray();
    }
}
