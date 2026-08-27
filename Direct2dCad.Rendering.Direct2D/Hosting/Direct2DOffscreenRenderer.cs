using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Ole;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

public static class Direct2DOffscreenRenderer
{
    public static Direct2DRenderedFrame Render(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        int pixelWidth,
        int pixelHeight,
        Direct2DOleDrawCallback? oleDrawCallback = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(options);
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        using var host = new Direct2DImageRenderHost();
        var imageSource = new OffscreenImageSource(pixelWidth, pixelHeight);
        host.AttachImageSource(imageSource);
        host.SetSize(pixelWidth, pixelHeight);
        // An offscreen frame is consumed once, so prepare entity resources now
        // instead of starting the interactive host's background preparation.
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(options);
        host.SetOleDrawCallback(oleDrawCallback);
        // Let the renderer use its complete immediate fallback. Warming retained
        // command-list and tile caches has no reuse value for printing and can take
        // an unbounded number of batches for large drawings.
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        var pixels = host.CaptureBackBufferPixels();
        var expectedLength = checked(pixelWidth * pixelHeight * 4);
        if (pixels.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Offscreen render returned {pixels.Length} bytes; expected {expectedLength}.");
        }

        return new Direct2DRenderedFrame(
            pixelWidth,
            pixelHeight,
            checked(pixelWidth * 4),
            pixels);
    }

    private sealed class OffscreenImageSource(int width, int height) : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = width;
        public int SurfaceHeight { get; private set; } = height;

        public void SetSize(int width, int height)
        {
            SurfaceWidth = width;
            SurfaceHeight = height;
        }

        public void SetSurface(nint surface9Ptr)
        {
        }

        public void Present(
            Action presentAction,
            IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();

        public void Invalidate()
        {
        }

        public void Invalidate(IntRect dirtyRect)
        {
        }

        public void Invalidate(IReadOnlyList<IntRect> dirtyRects)
        {
        }
    }
}

public sealed record Direct2DRenderedFrame(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);
