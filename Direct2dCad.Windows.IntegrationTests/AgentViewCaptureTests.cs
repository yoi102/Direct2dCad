using System.IO;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.wpf.Services.Importing;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class AgentViewCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureProducesActualModelAndPaperSpacePixels(bool paper)
    {
        var document = CadDocument.Create("Agent visual verification");
        document.AddCircle(new(30, 30), 15);
        document.AddLine(new(0, 0), new(70, 60));
        var viewport = new CadViewport();
        viewport.SetSize(512, 384);
        viewport.SetView(4, new(50, 330));
        LayoutId? layoutId = null;
        if (paper)
        {
            var layout = document.Layouts.Values.Single();
            layoutId = layout.Id;
            document.AddLayoutViewport(layout.Id, new(20, 20, 180, 120), new(30, 30), 2);
            viewport.SetView(0.9, new(50, 330));
        }
        var service = new CadViewCaptureService();
        var image = await service.CaptureAsync(new(document, viewport, new CadRenderOptions
        {
            ActiveLayoutId = layoutId, ActiveOwnerBlockId = layoutId is { } id ? document.GetLayout(id).PaperSpaceBlockId : BlockId.ModelSpace,
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false
        }, 512, 384), default);
        Assert.Equal("image/png", image.MimeType);
        using var stream = new MemoryStream(image.Data);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Single();
        Assert.Equal(512, frame.PixelWidth);
        Assert.Equal(384, frame.PixelHeight);
        var stride = frame.PixelWidth * ((frame.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        Assert.True(pixels.Distinct().Count() > 4, "Rendered pixels must contain drawing geometry, not a uniform placeholder.");
        var directory = Environment.GetEnvironmentVariable("DIRECT2DCAD_TEST_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, paper ? "agent-paper-preview.png" : "agent-model-preview.png"), image.Data);
        }
    }

    [Fact]
    public async Task CaptureRejectsCancelledRequests()
    {
        var viewport = new CadViewport();
        viewport.SetSize(128, 128);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CadViewCaptureService().CaptureAsync(
            new(CadDocument.Create("Cancelled"), viewport, new(), 128, 128), cancellation.Token));
    }
}
