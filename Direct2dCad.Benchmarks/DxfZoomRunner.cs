using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

/// <summary>Repeatable native zoom trace for a supplied DXF; does not measure WPF input/composition latency.</summary>
internal static class DxfZoomRunner
{
    private sealed record Frame(string Mode, int Iteration, string Direction, int Step, double Zoom,
        double ElapsedMilliseconds, long AllocatedBytes, CadRenderStatistics Statistics);

    public static void Run(string sourcePath, string outputDirectory)
    {
        var source = Path.GetFullPath(sourcePath); var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew();
        var imported = new CadDxfStorage().Import(source, CadUnit.Millimeter);
        var importMilliseconds = watch.Elapsed.TotalMilliseconds;
        var data = BenchmarkDocumentFactory.FromDocument(imported.Document);
        var frames = new List<Frame>(); var summaries = new List<object>();
        var diagnostic = Environment.GetEnvironmentVariable("CAD_DXF_ZOOM_DIAGNOSTIC") == "1";
        var modes = diagnostic
            ? new[] { "no-background", "no-polyline", "no-composite", "no-circle", "empty", "no-large" }
                .Concat(data.Document.Entities.Values.OfType<CadPolyline>().OrderByDescending(p => p.Points.Count)
                    .Take(4).Select(p => "only-" + p.Id)).ToArray()
            : new[] { "default", "no-realizations", "no-lod" };
        foreach (var mode in modes)
        {
            using var render = new BenchmarkRenderSession(data, levelOfDetail: mode != "no-lod", surfaceWidth: 516, surfaceHeight: 384);
            var host = render.RenderHost; var viewport = render.Viewport;
            host.SetRenderOptions(new CadRenderOptions
            {
                DrawGrid = mode != "no-background", DrawOrigin = mode != "no-background", DrawGripHandles = false,
                IsLevelOfDetailEnabled = mode != "no-lod", EnableGeometryRealizations = !diagnostic && mode != "no-realizations",
                HiddenEntityIds = data.Document.Entities.Values.Where(e => mode.StartsWith("only-") && e.Id.ToString() != mode[5..] ||
                    mode == "no-large" && e is CadPolyline { Points.Count: >= 512 } || mode == "empty" ||
                    mode == "no-polyline" && e is CadPolyline || mode == "no-composite" && e is CadCompositePath ||
                    mode == "no-circle" && e is CadCircle).Select(e => e.Id).ToHashSet(),
                EntityBoundsQueryInto = (owner, bounds, ids) => render.SpatialIndex.Query(owner, bounds, ids)
            });
            render.WarmUp();
            var zoom = viewport.Zoom; var offset = viewport.Offset;
            // Approximate center of the user's highlighted power-connector area, in drawing millimeters.
            var anchorWorld = new CadPointD(64, 40); var anchor = viewport.WorldToScreen(anchorWorld);
            SaveImage("fit-" + mode);
            for (var iteration = 1; iteration <= 3; iteration++)
            {
                viewport.SetView(zoom, offset); host.Render(CadRenderInvalidation.Full);
                foreach (var direction in new[] { "in", "out" })
                {
                    for (var step = 1; step <= 32; step++)
                    {
                        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                        viewport.ZoomAt(anchor, direction == "in" ? 1.1 : 1 / 1.1);
                        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
                        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        frames.Add(new(mode, iteration, direction, step, viewport.Zoom, elapsed, allocated, host.RenderStatistics));
                        if (iteration == 1 && direction == "in" && step == 16) SaveImage("detail-" + mode);
                    }
                }
            }
            var measured = frames.Where(f => f.Mode == mode).ToArray();
            var summary = new
            {
                mode, using_warp = host.UsingWarp, width = 516, height = 384,
                anchor_world = anchorWorld, anchor_screen = anchor, fitted_zoom = zoom,
                frame_count = measured.Length, median_ms = Percentile(measured.Select(f => f.ElapsedMilliseconds), .5),
                p95_ms = Percentile(measured.Select(f => f.ElapsedMilliseconds), .95), max_ms = measured.Max(f => f.ElapsedMilliseconds),
                median_cache_ms = Percentile(measured.Select(f => f.Statistics.CachePreparationMilliseconds), .5),
                median_submission_ms = Percentile(measured.Select(f => f.Statistics.CpuEntitySubmissionMilliseconds), .5),
                median_surface_ms = Percentile(measured.Select(f => f.Statistics.SurfaceDrawMilliseconds), .5),
                max_visible_entities = measured.Max(f => f.Statistics.VisibleEntityCount),
                max_realization_builds = measured.Max(f => f.Statistics.GeometryRealizationBuildCount),
                median_allocated_bytes = Percentile(measured.Select(f => (double)f.AllocatedBytes), .5)
            };
            summaries.Add(summary); Console.WriteLine(JsonSerializer.Serialize(summary));

            void SaveImage(string name)
            {
                var pixels = host.CaptureBackBufferPixels();
                var image = BitmapSource.Create(516, 384, 96, 96, PixelFormats.Bgra32, null, pixels, 516 * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
            }
        }
        var report = new
        {
            recorded_at = DateTimeOffset.UtcNow, source, source_bytes = new FileInfo(source).Length,
            source_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), import_ms = importMilliseconds,
            imported = imported.Imported, unsupported = imported.Unsupported,
            entity_types = data.Document.Entities.Values.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
            largest_paths = data.Document.Entities.Values.OrderByDescending(e => e switch
            { CadPolyline p => p.Points.Count, CadCompositePath p => p.Segments.Count, _ => 0 })
                .Take(20).Select(e => new { id = e.Id.ToString(), type = e.GetType().Name, bounds = e.Bounds,
                    complexity = e switch { CadPolyline p => p.Points.Count, CadCompositePath p => p.Segments.Count, _ => 0 } }),
            bounds = data.Bounds, summaries, frames,
            limitations = "Native hardware/WARP Direct2D host with a 516x384 offscreen Present sink; full redraw per zoom tick, default screen-constant strokes, no WPF mouse/composition latency. Three repeats, 32 zoom-in and 32 zoom-out ticks around (64,40) mm. Input DXF is read-only; missing units explicitly use millimeters."
        };
        File.WriteAllText(Path.Combine(output, "zoom.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Percentile(IEnumerable<double> values, double proportion)
    {
        var sorted = values.Order().ToArray(); return sorted[(int)Math.Floor((sorted.Length - 1) * proportion)];
    }
}
