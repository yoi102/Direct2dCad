using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;

namespace Direct2dCad.Avalonia;

internal sealed class OffscreenGpuReport
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ExecutableSha256 { get; set; } = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!)));
    public bool NativeAot { get; set; } = !RuntimeFeature.IsDynamicCodeSupported;
    public bool ApplicationWindowsCreated { get; set; }
    public int SharedFrames { get; set; }
    public long CpuReadbacks { get; set; }
    public int LargeDrawingEntities { get; set; }
    public double LargeDrawingFirstFrameMilliseconds { get; set; }
    public double GpuEnqueueMeanMilliseconds { get; set; }
    public double GpuCompletionMeanMilliseconds { get; set; }
    public double CpuReadbackMeanMilliseconds { get; set; }
    public int CpuReadbackBenchmarkFrames { get; set; }
    public string? Failure { get; set; }
}
[JsonSerializable(typeof(OffscreenGpuReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class GpuReportJsonContext : JsonSerializerContext;

// Starts only Avalonia's dispatcher and GPU compositor, without any application
// windows. This exercises actual ANGLE/Skia imports even though Headless uses a CPU backend.
internal static class OffscreenGpuProbe
{
    public static int Run(string output)
    {
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        var result = new OffscreenGpuReport();
        using var stopped = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (!result.NativeAot) throw new InvalidOperationException("GPU validation must run from the NativeAOT executable.");
                var compositor = Compositor.TryGetDefaultCompositor() ?? throw new InvalidOperationException("Windows GPU compositor is unavailable.");
                var interop = await compositor.TryGetCompositionGpuInterop();
                if (interop is null || !GpuCanvasPresenter.Supports(interop)) throw new InvalidOperationException("Windows compositor does not support shared D3D11 keyed-mutex images.");
                using var host = new Direct2DImageRenderHost(); var source = new ProbeSource(); host.AttachImageSource(source);
                var drawing = CadDocument.Create("GPU sharing fixture"); drawing.AddCircle(new CadPointD(0, 0), 12);
                var viewport = new CadViewport(); host.SetScene(drawing, viewport);
                using var surface = compositor.CreateDrawingSurface();
                foreach (var (width, height) in new[] { (256, 160), (512, 192), (128, 96) })
                {
                    host.SetSize(width, height); viewport.SetSize(width, height); viewport.SetView(3, new CadPointD(width / 2.0, height / 2.0));
                    host.Render(CadRenderInvalidation.Full, true);
                    using var shared = host.CreateSharedGpuFrame();
                    var imported = interop.ImportImage(new PlatformHandle(shared.SharedHandle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                        new PlatformGraphicsExternalImageProperties { Width = width, Height = height, TopLeftOrigin = true, Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm });
                    try
                    {
                        await imported.ImportCompleted;
                        for (var frame = 0; frame < 4; frame++)
                        {
                            if (!host.TryPublishSharedGpuFrame(shared)) throw new InvalidOperationException("Producer could not acquire the reusable GPU texture.");
                            if (host.TryPublishSharedGpuFrame(shared)) throw new InvalidOperationException("Producer overwrote a texture before consumer handoff.");
                            await surface.UpdateWithKeyedMutexAsync(imported, 1, 0); result.SharedFrames++;
                        }
                    }
                    finally { await imported.DisposeAsync(); }
                }
                result.CpuReadbacks = host.CpuReadbackCount;
                if (result.CpuReadbacks != 0) throw new InvalidOperationException("Shared GPU presentation performed a CPU readback.");
                var large = CadDocument.Create("100000 entity GPU benchmark");
                for (var index = 0; index < 100000; index++)
                { var x = index % 1000; var y = index / 1000; large.AddLine(new CadPointD(x, y), new CadPointD(x + .8, y + .6)); }
                host.SetSize(1024, 768); viewport.SetSize(1024, 768); viewport.SetView(1, new CadPointD(12, 434));
                host.SetRenderOptions(new CadRenderOptions { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false, IsLevelOfDetailEnabled = false });
                var timer = System.Diagnostics.Stopwatch.StartNew(); host.SetScene(large, viewport); host.Render(CadRenderInvalidation.Full, true);
                result.LargeDrawingFirstFrameMilliseconds = timer.Elapsed.TotalMilliseconds;
                result.LargeDrawingEntities = host.RenderStatistics.VisibleEntityCount;
                if (result.LargeDrawingEntities != 100000) throw new InvalidOperationException("Large drawing fixture did not submit every visible entity.");
                using var largeShared = host.CreateSharedGpuFrame();
                var largeImage = interop.ImportImage(new PlatformHandle(largeShared.SharedHandle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                    new PlatformGraphicsExternalImageProperties { Width = 1024, Height = 768, TopLeftOrigin = true, Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm });
                try
                {
                    await largeImage.ImportCompleted;
                    for (var frame = 0; frame < 8; frame++)
                    {
                        timer.Restart(); if (!host.TryPublishSharedGpuFrame(largeShared)) throw new InvalidOperationException("Large drawing GPU handoff failed.");
                        var completed = surface.UpdateWithKeyedMutexAsync(largeImage, 1, 0);
                        result.GpuEnqueueMeanMilliseconds += timer.Elapsed.TotalMilliseconds;
                        await completed; result.GpuCompletionMeanMilliseconds += timer.Elapsed.TotalMilliseconds; result.SharedFrames++;
                    }
                }
                finally { await largeImage.DisposeAsync(); }
                result.GpuEnqueueMeanMilliseconds /= 8; result.GpuCompletionMeanMilliseconds /= 8;
                if (host.CpuReadbackCount != 0) throw new InvalidOperationException("GPU benchmark read back pixels on the CPU.");
                // Measure the previous transfer path separately; these reads belong only to this baseline.
                for (var frame = 0; frame < 8; frame++)
                {
                    timer.Restart(); var pixels = host.CaptureBackBufferPixels();
                    if (pixels.Length != 1024 * 768 * 4) throw new InvalidOperationException("CPU baseline returned an incomplete frame.");
                    result.CpuReadbackMeanMilliseconds += timer.Elapsed.TotalMilliseconds; result.CpuReadbackBenchmarkFrames++;
                }
                result.CpuReadbackMeanMilliseconds /= result.CpuReadbackBenchmarkFrames;
                if (App.Window is not null) throw new InvalidOperationException("The offscreen GPU probe created an application window.");
            }
            catch (Exception error) { result.Failure = error.ToString(); }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(result, GpuReportJsonContext.Default.OffscreenGpuReport));
                stopped.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(stopped.Token);
        return result.Failure is null ? 0 : 1;
    }
    private sealed class ProbeSource : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = 256;
        public int SurfaceHeight { get; private set; } = 160;
        public void SetSize(int width, int height) { SurfaceWidth = width; SurfaceHeight = height; }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
    }
}
