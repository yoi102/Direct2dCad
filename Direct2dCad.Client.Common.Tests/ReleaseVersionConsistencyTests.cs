using System.Text.RegularExpressions;

namespace Direct2dCad.Client.Common.Tests;

/// <summary>
/// The product version lives in Directory.Build.props, but release scripts, MSI projects, docs and the
/// packaging workflow still carry their own default. A version bump must update them together.
/// </summary>
public sealed class ReleaseVersionConsistencyTests
{
    private static readonly Regex DefaultVersion = new(
        @"(?:ReleaseVersion[^0-9\r\n]{0,40}|github-release-|Direct2dCad-)(?<version>\d+\.\d+\.\d+)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ReleaseDefaultsMatchTheProductVersion()
    {
        var root = FindRepositoryRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var product = Regex.Match(props, @"<Direct2dCadVersion>(?<version>[^<]+)</Direct2dCadVersion>").Groups["version"].Value;
        Assert.Matches(@"^\d+\.\d+\.\d+$", product);

        var files = new[]
            {
                Directory.EnumerateFiles(Path.Combine(root, "scripts", "delivery"), "*.ps1"),
                Directory.EnumerateFiles(Path.Combine(root, "Direct2dCad.Avalonia"), "*.ps1"),
                Directory.EnumerateFiles(Path.Combine(root, "installer"), "*.wixproj", SearchOption.AllDirectories),
                Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml"),
                [Path.Combine(root, "Direct2dCad.Avalonia", "README.md")]
            }
            .SelectMany(paths => paths)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var found = files
            .SelectMany(path => DefaultVersion.Matches(File.ReadAllText(path))
                .Select(match => (File: Path.GetRelativePath(root, path), Version: match.Groups["version"].Value)))
            .ToArray();

        // Guards against the scan silently matching nothing after a rename.
        Assert.True(found.Length >= 9, $"Expected the release defaults to be found, got {found.Length}.");
        Assert.All(found, item => Assert.True(item.Version == product,
            $"{item.File} defaults to {item.Version}, but Directory.Build.props says {product}."));
    }

    private static string FindRepositoryRoot()
    {
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_REPOSITORY_ROOT") ?? AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Direct2dCad.slnx")))
            directory = Directory.GetParent(directory)?.FullName ?? throw new IOException("Repository root not found.");
        return directory;
    }
}
