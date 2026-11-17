using Direct2dCad.Application.Tools;
using Direct2dCad.Mcp;

namespace Direct2dCad.Application.Tests;

public sealed class CadMcpAnnotationTests
{
    private static readonly HashSet<string> CatalogNames =
        CadWorkspaceToolExecutor.ToolDefinitions.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string> AnnotationSets => new() { "ReadOnly", "Additive", "OpenWorld" };

    [Theory]
    [MemberData(nameof(AnnotationSets))]
    public void EveryAnnotatedToolExistsInTheCatalog(string set)
    {
        // A renamed or removed tool must not leave a stale annotation that silently stops applying.
        var names = set switch
        {
            "ReadOnly" => CadMcpServer.ReadOnlyTools,
            "Additive" => CadMcpServer.AdditiveTools,
            _ => CadMcpServer.OpenWorldTools
        };
        Assert.DoesNotContain(names, name => !CatalogNames.Contains(name));
    }

    [Fact]
    public void ReadOnlyAndAdditiveToolsAreDisjoint() =>
        Assert.DoesNotContain(CadMcpServer.ReadOnlyTools, CadMcpServer.AdditiveTools.Contains);

    [Fact]
    public void ServerVersionComesFromTheProductVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(CadMcpServer.ServerVersion));
        Assert.NotEqual("0.0.0", CadMcpServer.ServerVersion);
        Assert.DoesNotContain('+', CadMcpServer.ServerVersion); // no source-revision suffix leaks to clients
    }
}
