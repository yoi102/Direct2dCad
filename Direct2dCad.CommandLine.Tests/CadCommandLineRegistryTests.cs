namespace Direct2dCad.CommandLine.Tests;

public sealed class CadCommandLineRegistryTests
{
    private sealed class Handler(string name, string aliases = "") : ICadCommandLineHandler
    {
        public CadCommandLineDescriptor Descriptor { get; } = new(name, aliases, name, name);
        public CadCommandLineResult Execute(CadCommandLineRequest request) => new(true, Descriptor.Name);
    }

    [Fact]
    public void ResolvesNamesAndAliasesCaseInsensitivelyWithPrefixes()
    {
        var registry = new CadCommandLineRegistry();
        var line = new Handler("LINE", "L, LI");
        registry.Register(line);

        foreach (var name in new[] { "line", "_LINE", ".l", " li " })
        {
            Assert.True(registry.TryResolve(name, out var resolved), name);
            Assert.Same(line, resolved);
        }
        Assert.False(registry.TryResolve("CIRCLE", out _));
    }

    [Fact]
    public void RejectsConflictingNamesButAllowsReRegisteringTheSameHandler()
    {
        var registry = new CadCommandLineRegistry();
        var line = new Handler("LINE", "L");
        registry.Register(line);
        registry.Register(line);
        Assert.Single(registry.Handlers);

        Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler("LAYER", "l")));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler("line")));
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void CompleteMatchesNamesAndAliasesAndHonoursTheLimit()
    {
        var registry = new CadCommandLineRegistry();
        registry.Register(new Handler("LINE", "L"));
        registry.Register(new Handler("LAYER", "LA"));
        registry.Register(new Handler("CIRCLE", "C"));
        registry.Register(new Handler("POLYLINE", "PL"));

        Assert.Equal(new[] { "LAYER", "LINE" }, registry.Complete("l"));
        Assert.Equal(new[] { "POLYLINE" }, registry.Complete("pl"));
        Assert.Single(registry.Complete("L", maximumCount: 1));
        Assert.Empty(registry.Complete("L", maximumCount: 0));
    }
}
