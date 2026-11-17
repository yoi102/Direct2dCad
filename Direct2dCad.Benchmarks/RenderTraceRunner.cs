using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

/// <summary>
/// C# port of scripts/benchmarks/Measure-RenderDocument.ps1 that needs no PowerShell 7, plus the two
/// wheel-zoom paths the canvas really uses: full renders per notch (the default) and snapshot preview.
/// Source is a .d2cad / .dxf path or "generated:lines:N" / "generated:mixed:N".
/// </summary>
internal static class RenderTraceRunner
{
    private const int Width = 1600, Height = 900;
    private static readonly string[] Modes = ["warm-full", "overlay-dirty", "pan", "zoom", "zoom-preview", "zoom-settle"];

    private sealed record Frame(int Iteration, string Mode, int Step, double Zoom, double ElapsedMilliseconds,
        long AllocatedBytes, CadRenderStatistics Statistics);

    public static void Run(string source, string outputDirectory, int iterations = 3, int frameCount = 64)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var (label, hash, load) = Describe(source);
        var frames = new List<Frame>();
        var rows = new List<object>();
        for (var iteration = 1; iteration <= iterations; iteration++)
        {
            var watch = Stopwatch.StartNew();
            var data = load();
            var loadMilliseconds = watch.Elapsed.TotalMilliseconds;
            var openStarted = Stopwatch.GetTimestamp();
            using var session = new BenchmarkRenderSession(data, levelOfDetail: true, progressivePreparation: true,
                surfaceWidth: Width, surfaceHeight: Height);
            var host = session.RenderHost;
            // RENDER_TRACE_WORLD_LINES=1 measures the world-space line strokes for comparison.
            host.DeviceSpaceLineStrokesEnabled = Environment.GetEnvironmentVariable("RENDER_TRACE_WORLD_LINES") != "1";
            var view = session.Viewport;
            double? firstPresent = null;
            bool pending;
            do
            {
                pending = host.PrepareRenderCacheStep();
                if (!host.HasPresentedScene && host.IsInitialViewReady)
                {
                    host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                    if (host.HasPresentedScene) firstPresent = Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds;
                }
                if (Stopwatch.GetElapsedTime(openStarted).TotalSeconds > 180)
                    throw new TimeoutException("Resource preparation exceeded 180 seconds.");
                Thread.Yield();
            } while (pending);
            var allReady = Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds;
            session.WarmUp();
            var zoom = view.Zoom;
            var offset = view.Offset;
            var dirty = CadRenderInvalidation.FromScreenRect(new CadScreenRect(680, 390, 240, 120));
            var center = new CadPointD(Width / 2.0, Height / 2.0);
            foreach (var mode in Modes)
            {
                view.SetView(zoom, offset);
                host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                session.WarmUp(1);
                for (var index = 0; index < frameCount; index++)
                {
                    var factor = index < frameCount / 2 ? 1.1 : 1 / 1.1;
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var started = Stopwatch.GetTimestamp();
                    switch (mode)
                    {
                        case "overlay-dirty":
                            host.Render(dirty, baseSceneChanged: false);
                            break;
                        case "pan":
                            view.PanScreen(new CadVectorD(index < frameCount / 2 ? 4 : -4, 0));
                            host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                            break;
                        case "zoom":
                            view.ZoomAt(center, factor);
                            host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                            break;
                        case "zoom-preview":
                            // Opt-in canvas path: one scaled snapshot per notch, the full render comes later.
                            host.BeginViewportInteraction();
                            view.ZoomAt(center, factor);
                            if (!host.RenderViewportInteractionPreview())
                                host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                            break;
                        case "zoom-settle":
                            // The frame drawn once the wheel stops, at a zoom the caches have not seen yet.
                            view.ZoomAt(center, factor);
                            host.EndViewportInteraction();
                            host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                            break;
                        default:
                            host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                            break;
                    }
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    frames.Add(new(iteration, mode, index + 1, view.Zoom, elapsed,
                        GC.GetAllocatedBytesForCurrentThread() - allocated, host.RenderStatistics));
                }
                host.EndViewportInteraction();
            }
            rows.Add(new
            {
                iteration, load_ms = loadMilliseconds, first_visible_present_ms = firstPresent,
                all_resources_ready_ms = allReady, entity_count = data.Document.Entities.Count,
                using_warp = host.UsingWarp,
                types = data.Document.Entities.Values.GroupBy(e => e.GetType().Name)
                    .OrderByDescending(g => g.Count()).Select(g => new { type = g.Key, count = g.Count() })
            });
            Console.WriteLine($"iteration {iteration}: load {loadMilliseconds:F0} ms, first present {firstPresent:F0} ms, ready {allReady:F0} ms");
        }

        var summaries = Modes.Select(mode =>
        {
            var measured = frames.Where(f => f.Mode == mode).ToArray();
            var times = measured.Select(f => f.ElapsedMilliseconds).ToArray();
            double Median(Func<Frame, double> value) => Percentile(measured.Select(value).ToArray(), .5);
            return new
            {
                mode, frame_count = measured.Length,
                median_ms = Percentile(times, .5), mean_ms = times.Average(), p95_ms = Percentile(times, .95), max_ms = times.Max(),
                median_allocated_bytes = Median(f => f.AllocatedBytes),
                median_entity_submissions = Median(f => f.Statistics.EntitySubmissionCount),
                max_entity_submissions = measured.Max(f => f.Statistics.EntitySubmissionCount),
                median_visible_entities = Median(f => f.Statistics.VisibleEntityCount),
                median_tile_replays = Median(f => f.Statistics.TileReplayCount),
                total_tile_builds = measured.Sum(f => f.Statistics.TileBuildCount),
                median_command_list_replays = Median(f => f.Statistics.CommandListReplayCount),
                total_command_list_builds = measured.Sum(f => f.Statistics.CommandListBuildCount),
                total_render_cache_misses = measured.Sum(f => f.Statistics.RenderCacheMissCount),
                total_realization_builds = measured.Sum(f => f.Statistics.GeometryRealizationBuildCount),
                median_cpu_submission_ms = Median(f => f.Statistics.CpuEntitySubmissionMilliseconds),
                median_visibility_query_ms = Median(f => f.Statistics.VisibilityQueryMilliseconds),
                median_entity_render_ms = Median(f => f.Statistics.EntityRenderMilliseconds),
                median_surface_draw_ms = Median(f => f.Statistics.SurfaceDrawMilliseconds),
                median_micro_candidates = Median(f => f.Statistics.MicroEntityCandidateCount),
                median_micro_representatives = Median(f => f.Statistics.MicroEntityRepresentativeCount)
            };
        }).ToArray();

        var report = new
        {
            label, source, source_sha256 = hash, recorded_at = DateTimeOffset.UtcNow,
            machine = Environment.MachineName, runtime = Environment.Version.ToString(), surface = $"{Width}x{Height}",
            rendering_assembly_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                typeof(Direct2dCad.Rendering.Direct2D.Hosting.Direct2DImageRenderHost).Assembly.Location))),
            rows, summaries,
            frames = frames.Select(f => new
            {
                f.Iteration, f.Mode, f.Step, f.Zoom, f.ElapsedMilliseconds, f.AllocatedBytes,
                f.Statistics.EntitySubmissionCount, f.Statistics.VisibleEntityCount, f.Statistics.TileReplayCount,
                f.Statistics.TileBuildCount, f.Statistics.CommandListReplayCount, f.Statistics.CommandListBuildCount,
                f.Statistics.RenderCacheMissCount, f.Statistics.GeometryRealizationBuildCount,
                f.Statistics.CpuEntitySubmissionMilliseconds, f.Statistics.VisibilityQueryMilliseconds
            }),
            limitations = "Native offscreen Direct2D host, LOD on, 1600x900. CPU wall clock includes EndDraw and the " +
                "shared-texture Present callback; no GPU timestamps, WPF composition, input latency or Dispatcher pacing."
        };
        File.WriteAllText(Path.Combine(output, "measurements.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var summary in summaries)
            Console.WriteLine(JsonSerializer.Serialize(summary));
        if (hash is not null && File.Exists(source) &&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash)
            throw new InvalidOperationException("The input document changed while the benchmark was running.");
    }

    private static (string Label, string? Hash, Func<BenchmarkDocumentData> Load) Describe(string source)
    {
        if (source.StartsWith("generated:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = source.Split(':');
            var kind = Enum.Parse<BenchmarkDocumentKind>(parts[1], ignoreCase: true);
            var count = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            return (source, null, () => BenchmarkDocumentFactory.Create(count, kind));
        }

        var path = Path.GetFullPath(source);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        return (Path.GetFileName(path), hash, () =>
        {
            CadDocument document = Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                ? new CadDxfStorage().Import(path, CadUnit.Millimeter).Document
                : new CadDocumentStorage().Load(path);
            return BenchmarkDocumentFactory.FromDocument(document);
        });
    }

    private static double Percentile(double[] values, double fraction)
    {
        if (values.Length == 0) return 0;
        var ordered = values.Order().ToArray();
        return ordered[(int)Math.Floor((ordered.Length - 1) * fraction)];
    }
}
