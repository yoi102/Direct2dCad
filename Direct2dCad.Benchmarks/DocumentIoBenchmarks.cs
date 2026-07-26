using BenchmarkDotNet.Attributes;
using Direct2dCad.Indexing;
using Direct2dCad.IO;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

[CadBenchmark]
public class DocumentIoBenchmarks
{
    internal const string DocumentEnvironmentVariable =
        "DIRECT2DCAD_BENCHMARK_DOCUMENT";
    private const string GeneratedDocument = "generated-20000-mixed";

    private readonly CadDocumentStorage _storage = new();
    private BenchmarkDocumentData _data = null!;
    private string _sourcePath = string.Empty;
    private string _savePath = string.Empty;
    private bool _ownsSourcePath;

    public IEnumerable<string> DocumentSources
    {
        get
        {
            var configuredPath = Environment.GetEnvironmentVariable(
                DocumentEnvironmentVariable);
            yield return string.IsNullOrWhiteSpace(configuredPath)
                ? GeneratedDocument
                : Path.GetFullPath(configuredPath);
        }
    }

    [ParamsSource(nameof(DocumentSources))]
    public string DocumentSource { get; set; } = GeneratedDocument;

    [GlobalSetup]
    public void Setup()
    {
        if (string.Equals(
                DocumentSource,
                GeneratedDocument,
                StringComparison.OrdinalIgnoreCase))
        {
            _data = BenchmarkDocumentFactory.Create(
                20_000,
                BenchmarkDocumentKind.Mixed);
            _sourcePath = CreateTemporaryPath("source");
            _storage.Save(_data.Document, _sourcePath);
            _ownsSourcePath = true;
        }
        else
        {
            if (!File.Exists(DocumentSource))
                throw new FileNotFoundException(
                    "Configured benchmark document was not found.",
                    DocumentSource);

            _sourcePath = DocumentSource;
            _data = BenchmarkDocumentFactory.FromDocument(
                _storage.Load(_sourcePath));
        }

        _savePath = CreateTemporaryPath("save");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_ownsSourcePath)
            DeleteIfExists(_sourcePath);
        DeleteIfExists(_savePath);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DocumentIo", "Load")]
    public int LoadDocument()
    {
        return _storage.Load(_sourcePath).Entities.Count;
    }

    [Benchmark]
    [BenchmarkCategory("DocumentIo", "LoadAsync")]
    public async Task<int> LoadDocumentAsync()
    {
        var document = await _storage.LoadAsync(_sourcePath).ConfigureAwait(false);
        return document.Entities.Count;
    }

    [Benchmark]
    [BenchmarkCategory("DocumentIo", "SectionRead")]
    public int ReadSettingsSection()
    {
        var settings = _storage.ReadSettings(_sourcePath);
        return HashCode.Combine(
            settings.AnglePrecision,
            settings.LengthPrecision,
            settings.GridSpacingX);
    }

    [Benchmark]
    [BenchmarkCategory("DocumentIo", "Save")]
    public long SaveDocument()
    {
        _storage.Save(_data.Document, _savePath);
        return new FileInfo(_savePath).Length;
    }

    [Benchmark]
    [BenchmarkCategory("DocumentIo", "SaveAsync")]
    public async Task<long> SaveDocumentAsync()
    {
        await _storage.SaveAsync(_data.Document, _savePath).ConfigureAwait(false);
        return new FileInfo(_savePath).Length;
    }

    [Benchmark]
    [BenchmarkCategory("OpenPipeline", "Cpu")]
    public int LoadAndBuildSpatialIndex()
    {
        var document = _storage.Load(_sourcePath);
        var index = new CadSpatialIndex();
        index.Rebuild(document);
        return index.Count;
    }

    [Benchmark]
    [BenchmarkCategory("OpenPipeline", "FirstFrame")]
    public long LoadIndexAndRenderFirstFrame()
    {
        var document = _storage.Load(_sourcePath);
        var data = BenchmarkDocumentFactory.FromDocument(document);
        using var session = new BenchmarkRenderSession(data);
        session.RenderHost.Render(
            CadRenderInvalidation.Full,
            baseSceneChanged: true);
        return session.CaptureFrameChecksum();
    }

    private static string CreateTemporaryPath(string purpose)
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"Direct2dCad.Benchmark.{purpose}.{Guid.NewGuid():N}.d2cad");
    }

    private static void DeleteIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
