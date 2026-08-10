using System.Text;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class AiTextFileReaderTests
{
    [Fact]
    public void Read_TextFileReturnsNameTypeAndContent()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("notes.md", "# Important notes");

        var result = AiTextFileReader.Read(path);

        Assert.Equal("notes.md", result.SourceName);
        Assert.Equal("text/markdown", result.ContentType);
        Assert.Equal("# Important notes", result.TextContent);
        Assert.False(result.IsImage);
    }

    [Fact]
    public void Read_TextFileDetectsUtf8Bom()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteBytes(
            "notes.txt",
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("中文内容")]);

        var result = AiTextFileReader.Read(path);

        Assert.Equal("中文内容", result.TextContent);
    }

    [Fact]
    public void Read_LargeTextFileIsTruncatedToContextSafeLength()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("large.txt", new string('x', AiTextFileReader.MaximumCharacters + 1));

        var result = AiTextFileReader.Read(path);

        Assert.NotNull(result.TextContent);
        Assert.StartsWith(new string('x', AiTextFileReader.MaximumCharacters), result.TextContent);
        Assert.Contains("truncated to fit the AI context window", result.TextContent);
    }

    [Fact]
    public void Read_RejectsInvalidUtf8()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteBytes("binary.bin", [0xC3, 0x28]);

        var exception = Assert.Throws<InvalidDataException>(() => AiTextFileReader.Read(path));

        Assert.Contains("UTF text files", exception.Message);
    }

    [Fact]
    public void Read_RejectsTextFileLargerThanMaximum()
    {
        using var files = new TemporaryFiles();
        var path = files.Create("large.txt");
        using (var stream = File.OpenWrite(path))
            stream.SetLength(AiTextFileReader.MaximumFileBytes + 1);

        var exception = Assert.Throws<InvalidDataException>(() => AiTextFileReader.Read(path));

        Assert.Contains("larger than 4 MB", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Read_RejectsEmptyPath(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => AiTextFileReader.Read(path!));
    }

    [Fact]
    public void Read_RejectsMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() => AiTextFileReader.Read("missing-notes.txt"));
    }

    [Fact]
    public void Read_MapsCommonSourceExtensionsToContentTypes()
    {
        using var files = new TemporaryFiles();
        var cases = new Dictionary<string, string>
        {
            ["data.json"] = "application/json",
            ["table.csv"] = "text/csv",
            ["script.py"] = "text/x-python",
            ["drawing.svg"] = "image/svg+xml",
            ["unknown.data"] = "text/plain"
        };

        foreach (var pair in cases)
        {
            var result = AiTextFileReader.Read(files.WriteText(pair.Key, "content"));
            Assert.Equal(pair.Value, result.ContentType);
        }
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"Direct2dCad.AiTextFileReaderTests.{Guid.NewGuid():N}");

        public TemporaryFiles() => Directory.CreateDirectory(_directory);

        public string Create(string fileName) => Path.Combine(_directory, fileName);

        public string WriteText(string fileName, string content)
        {
            var path = Create(fileName);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        public string WriteBytes(string fileName, byte[] content)
        {
            var path = Create(fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
