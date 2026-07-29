using Direct2dCad.ViewModels.AI;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadAiCommandLineServiceTests
{
    [Fact]
    public async Task ToolsAndToolHelp_ExposeTheSharedAiToolCatalog()
    {
        var service = new CadAiCommandLineService(new EmptyWorkspace());

        var tools = await service.TryExecuteAsync("TOOLS circle");
        var help = await service.TryExecuteAsync("TOOLHELP add_circle");

        Assert.NotNull(tools);
        Assert.True(tools.Success);
        Assert.Contains("add_circle", tools.Message, StringComparison.Ordinal);
        Assert.NotNull(help);
        Assert.True(help.Success);
        Assert.Contains("center_x", help.Message, StringComparison.Ordinal);
        Assert.Contains("TOOL add_circle", help.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectToolName_ExecutesThroughWorkspaceToolExecutor()
    {
        var service = new CadAiCommandLineService(new EmptyWorkspace());

        var execution = await service.TryExecuteAsync("list_documents {}");

        Assert.NotNull(execution);
        Assert.True(execution.Success);
        Assert.Contains("\"documents\": []", execution.Message, StringComparison.Ordinal);
        Assert.Contains("\"success\": true", execution.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltInCollision_RequiresExplicitToolPrefix()
    {
        var service = new CadAiCommandLineService(new EmptyWorkspace());

        var builtIn = await service.TryExecuteAsync("undo");
        var explicitTool = await service.TryExecuteAsync("TOOL undo {}");

        Assert.Null(builtIn);
        Assert.NotNull(explicitTool);
        Assert.False(explicitTool.Success);
        Assert.Contains("No CAD document", explicitTool.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_IncludesDirectAndPrefixedToolSyntax()
    {
        var service = new CadAiCommandLineService(new EmptyWorkspace());

        Assert.Contains("add_circle", service.Complete("add_c"));
        Assert.Contains("TOOL add_circle", service.Complete("TOOL add_c"));
        Assert.Contains("TOOLS", service.Complete("TOO"));
    }

    [Fact]
    public async Task NonJsonArguments_ReturnActionableError()
    {
        var service = new CadAiCommandLineService(new EmptyWorkspace());

        var execution = await service.TryExecuteAsync("add_circle center_x=0");

        Assert.NotNull(execution);
        Assert.False(execution.Success);
        Assert.Contains("JSON object", execution.Message, StringComparison.Ordinal);
    }

    private sealed class EmptyWorkspace : ICadAiWorkspaceService
    {
        public IReadOnlyList<CadAiWorkspaceDocument> GetDocuments() => [];
        public CadAiWorkspaceDocument? GetActiveDocument() => null;
        public CadAiWorkspaceDocument GetRequiredDocument(string documentId) =>
            throw new ArgumentException($"Open document not found: {documentId}");
        public CadAiWorkspaceDocument CreateDocument(string? name) =>
            throw new NotSupportedException();
        public Task<CadAiWorkspaceDocument> OpenDocumentAsync(
            string filePath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public bool ActivateDocument(string documentId) => false;
        public bool RenameDocument(string documentId, string name) => false;
        public Task<bool> SaveDocumentAsync(
            string documentId,
            string? filePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CloseDocumentAsync(string documentId) => Task.FromResult(false);
    }
}
