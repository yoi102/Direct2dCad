using System.Text.Json;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DocumentNameToolTests
{
    private static async Task<JsonElement> Call(ICadToolWorkspace workspace, string name, object args)
    {
        using var result = JsonDocument.Parse(await new CadWorkspaceToolExecutor(workspace)
            .ExecuteAsync(new(Guid.NewGuid().ToString(), name, JsonSerializer.Serialize(args)), default));
        return result.RootElement.Clone();
    }

    [Theory]
    [InlineData("", "cannot be empty")]
    [InlineData("   ", "cannot be empty")]
    [InlineData("plan/section", "'/'")]
    [InlineData("a\0b", "U+0000")]
    public void InvalidNamesAreExplained(string name, string expected)
    {
        var problem = EditorTabViewModel.DescribeInvalidDocumentName(name);
        Assert.NotNull(problem);
        Assert.Contains(expected, problem);
    }

    [Theory]
    [InlineData("Floor plan")]
    [InlineData("图纸 図面 A-1")]
    public void UsableNamesPass(string name) => Assert.Null(EditorTabViewModel.DescribeInvalidDocumentName(name));

    [Fact]
    public async Task CreateDocumentNamesTheOffendingCharacterAndLeavesNoTabBehind()
    {
        using var context = new MainWindowTestContext();
        var workspace = context.CreateToolWorkspace();

        var refused = await Call(workspace, "create_document", new { name = "plan/section" });
        Assert.False(refused.GetProperty("success").GetBoolean());
        var error = refused.GetProperty("error").GetString()!;
        Assert.Contains("'/'", error);
        Assert.DoesNotContain("empty", error);
        Assert.Empty(workspace.GetDocuments());

        var created = await Call(workspace, "create_document", new { name = "图纸 A-1" });
        Assert.True(created.GetProperty("success").GetBoolean());
        Assert.Equal("图纸 A-1", Assert.Single(workspace.GetDocuments()).Name);
    }

    [Fact]
    public async Task RenameDocumentExplainsWhyANameIsRefused()
    {
        using var context = new MainWindowTestContext();
        var workspace = context.CreateToolWorkspace();
        Assert.True((await Call(workspace, "create_document", new { name = "Before" })).GetProperty("success").GetBoolean());

        var refused = await Call(workspace, "rename_document", new { name = "a/b" });
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("'/'", refused.GetProperty("error").GetString());
        Assert.Equal("Before", Assert.Single(workspace.GetDocuments()).Name);

        Assert.True((await Call(workspace, "rename_document", new { name = "After" })).GetProperty("success").GetBoolean());
        Assert.Equal("After", Assert.Single(workspace.GetDocuments()).Name);
    }
}
